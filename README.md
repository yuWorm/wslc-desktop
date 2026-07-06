# WSLC Desktop

[Chinese documentation](README.zh-CN.md)

WSLC Desktop is a WinUI 3 desktop app for managing Windows system-level containers through WSLC. It gives you a GUI for containers, images, volumes, published ports, Compose projects, logs, terminals, daemon health, runtime settings, and Docker-compatible command-line access.

The app is built around `wslcd-desktop`, a current-user background daemon. The GUI talks to the daemon through a native WSLC Desktop API. Docker CLI, Docker Compose, and compatible tools can talk to the daemon through a Docker Engine API-compatible named pipe.

## What It Is

WSLC Desktop is not Docker Desktop. It is a WSLC-first control plane with an optional Docker API compatibility layer.

- The default backend is the WSLC backend. It uses `wslc.exe`, so it manages the same containers, images, and volumes that you see in a terminal with `wslc`.
- The Docker-compatible endpoint is exposed on `npipe:////./pipe/wslc-desktop-docker`.
- The app also has an opt-in Docker API backend for connecting the GUI to another Docker-compatible engine, such as Docker Desktop or another named-pipe Docker API host.
- The GUI does not automatically switch your global Docker context. You choose when to create or use the `wslc-desktop` Docker context.

## Requirements

Required:

- Windows with WSL support.
- A recent WSL build that includes `wslc.exe`.
- WSLC Desktop and `wslcd-desktop` running as the same Windows user.

If WSL is missing, run:

```powershell
wsl --install
```

If WSL is installed but `wslc.exe` is missing, update WSL:

```powershell
wsl --update
```

Recommended:

- Docker CLI (`docker.exe`) for command-line access.
- Docker Compose (`docker-compose.exe` or the Docker CLI plugin under `cli-plugins\docker-compose.exe`) for Compose workflows.

Docker CLI and Compose are optional for the GUI, but required if you want to run `docker` or `docker compose` against WSLC Desktop.

## Install

GitHub Releases publish two Windows distributions:

- `wslc-desktop-<version>-win-x64-setup.exe` or `wslc-desktop-<version>-win-arm64-setup.exe`: per-user installer. This is the recommended option.
- `wslc-desktop-<version>-win-x64-portable.zip` or `wslc-desktop-<version>-win-arm64-portable.zip`: portable layout. Extract it to a writable folder and run `wslc-desktop.exe`.

MSIX packages are not published. The Setup.exe installer avoids the MSIX certificate trust step, but unsigned beta installers may still show a Windows SmartScreen warning.

## First Run

On startup, WSLC Desktop checks the host before starting `wslcd-desktop`.

1. If `wslc.exe` is available, the app starts the daemon and loads the GUI.
2. If WSL is missing, the app shows a required dialog with `wsl --install`. This step blocks normal use.
3. If WSL is present but `wslc.exe` is missing, the app treats it as an older WSL build and shows `wsl --update`. This step also blocks normal use.
4. If Docker CLI is missing, the app shows an optional dialog. You can dismiss it and keep using the GUI.

To install Docker CLI helpers later, open `Settings -> CLI tools`.

The installer can:

- Download the latest Docker CLI zip from `https://download.docker.com/win/static/stable/x86_64/` and install only `docker.exe`.
- Import a Docker CLI zip manually when the network is unreliable.
- Download the latest Docker Compose release from GitHub.
- Import a local Compose executable manually.
- Install Compose as both `bin\docker-compose.exe` and `bin\cli-plugins\docker-compose.exe`.
- Add the tool `bin` directory to the user PATH or the system PATH.

## Using the GUI

The main pages are:

- Containers: create, start, stop, restart, remove, inspect, view logs, and open terminals.
- Images: list, pull, remove, and track pull tasks.
- Volumes: list, create, and remove named data volumes.
- Networks: inspect published container ports and open endpoint URLs.
- Compose: open a Compose file, preview service plans, create and start services.
- Settings: configure runtime provider, WSLC proxy and mirror settings, Docker API backend settings, daemon controls, CLI tool installation, startup behavior, language, and diagnostics.

Daemon controls are available in Settings and in the title-bar runtime status flyout. The system tray also exposes quick actions. Closing the window hides the app to the tray; choosing Exit from the tray exits the UI and stops `wslcd-desktop`.

## Using Docker CLI

WSLC Desktop exposes a Docker Engine API-compatible endpoint at:

```text
npipe:////./pipe/wslc-desktop-docker
```

Create a Docker context:

```powershell
docker context create wslc-desktop --docker "host=npipe:////./pipe/wslc-desktop-docker"
```

Use the context per command:

```powershell
docker --context wslc-desktop version
docker --context wslc-desktop ps -a
docker --context wslc-desktop images
docker --context wslc-desktop pull nginx:alpine
docker --context wslc-desktop run --name web -p 18080:80 nginx:alpine
docker --context wslc-desktop logs web
docker --context wslc-desktop exec web sh -lc "uname -a"
docker --context wslc-desktop rm -f web
```

If you want this context to become your default Docker CLI target:

```powershell
docker context use wslc-desktop
```

Use the explicit `--context` form when you do not want to change global Docker CLI state.

## Backend Modes

WSLC Desktop has two runtime provider modes.

| Area | WSLC backend | Docker API backend |
| --- | --- | --- |
| Default | Yes | No, explicit opt-in |
| Runtime source | `wslc.exe` | Docker-compatible API host |
| Typical host | Local WSLC control plane | Docker Desktop, another named-pipe engine, or explicitly allowed TCP host |
| Resource scope | Same resources as `wslc` CLI | Resources from the configured Docker-compatible engine |
| Container lifecycle | Supported | Supported where the external engine supports it |
| Images | List, pull, remove | List, pull, remove |
| Volumes | Named volumes and bind mounts | Provider-dependent Docker API behavior |
| Networks | Published-port view; Docker-style user-defined network CRUD is not supported | Provider-dependent; external engines may support more |
| Logs | Buffered logs; follow depends on provider capability | Docker API logs where supported by the engine |
| Stats | One-shot stats; stats streaming is limited by provider capability | Provider-dependent |
| Exec | Non-interactive exec is supported; interactive TTY is limited by provider capability | Provider-dependent |
| Build/push/search | Build and push are not supported in the current product path; registry search is explicitly unsupported | Provider-dependent, but the GUI currently focuses on runtime management |

Use the WSLC backend when you want to manage Windows WSLC resources. Use the Docker API backend when you intentionally want the GUI to point at another Docker-compatible runtime.

## Docker Compose with the WSLC Backend

Compose works through the Docker API compatibility layer. The safest workflow is:

```powershell
docker --context wslc-desktop compose config
docker --context wslc-desktop compose up -d
docker --context wslc-desktop compose ps
docker --context wslc-desktop compose down
```

Recommended service shape:

```yaml
services:
  web:
    image: nginx:alpine
    network_mode: default
    ports:
      - "18080:80"
    environment:
      NGINX_ENTRYPOINT_QUIET_LOGS: "1"
    volumes:
      - web-data:/usr/share/nginx/html

volumes:
  web-data:
```

Supported common service fields include `image`, `command`, `environment`, `ports`, `volumes`, `labels`, `depends_on`, and `network_mode: default`.

Important notes for the WSLC backend:

- Add `network_mode: default` to services when possible. Docker-style user-defined network creation is not supported by the WSLC backend, so Compose files that rely on implicit project networks can fail.
- `restart` / restart policy is limited because the WSLC provider does not currently expose full Docker restart-policy behavior.
- Logs are buffered for the WSLC provider; live `follow` behavior depends on provider capability.
- `docker stats --no-stream` is the safest stats mode. Default stats streaming may return an explicit unsupported response when the active provider does not support stats streaming.
- Non-interactive `docker exec` is supported. Interactive TTY behavior is limited by provider capability.
- `build`, user-defined `networks`, `secrets`, `configs`, `deploy`, image push, and registry search are not part of the current WSLC backend path.

## Troubleshooting

### `wslc.exe` is missing

Run:

```powershell
wsl --update
```

If WSL itself is missing, run:

```powershell
wsl --install
```

Restart WSLC Desktop after the command finishes.

### Docker CLI is not found

Open `Settings -> CLI tools`.

Use one of these options:

- Install Docker CLI online.
- Install Docker CLI from a local zip.
- Install Compose online.
- Select a local Compose exe.
- Add the tool `bin` directory to user PATH or system PATH.

Open a new terminal after updating PATH.

### Docker context cannot connect

Check that `wslcd-desktop` is running in the GUI title-bar status flyout or in Settings. Then recreate the context if needed:

```powershell
docker context rm wslc-desktop
docker context create wslc-desktop --docker "host=npipe:////./pipe/wslc-desktop-docker"
docker --context wslc-desktop ps -a
```

### Compose fails while creating a network

Use `network_mode: default` in each service or remove user-defined `networks` from the Compose file. The WSLC backend intentionally returns a Docker-style unsupported error for Docker network create/delete operations.

### A runtime setting changed but behavior did not update

Restart `wslcd-desktop` from Settings or from the title-bar runtime status flyout. Proxy, image mirror, runtime provider, and Docker API backend settings are applied when the daemon starts.

### Need logs or diagnostics

Use Settings to export diagnostics. Include daemon health, provider status, recent operations, and relevant Docker/Compose command output when reporting an issue.

## Related Documentation

- WSLC documentation: https://github.com/MicrosoftDocs/wsl/blob/main/WSL/wsl-container.md
- Docker static Windows binaries: https://download.docker.com/win/static/stable/x86_64/
- Docker Compose releases: https://github.com/docker/compose/releases
- Local Compose compatibility notes: `docs/COMPOSE_COMPATIBILITY.md`
- Local Docker API matrix: `docs/DOCKER_API_COMPATIBILITY_MATRIX.md`
- Local runtime provider matrix: `docs/RUNTIME_PROVIDER_MATRIX.md`
