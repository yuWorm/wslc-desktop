# Runtime Provider Matrix

This matrix constrains how WSLC Desktop selects and previews container runtimes.

| Provider | Status | Host | Scope | Notes |
| --- | --- | --- | --- | --- |
| `wslc-cli` | Default | `wslc.exe` | Real Windows WSLC resources | This remains the default because it sees the same containers, images, volumes, and ports as the user's terminal. |
| `DockerApiProvider` | Experimental | Named pipe | Docker API compatible runtimes | Use `npipe:////./pipe/docker_engine` or another explicit Windows named pipe host. The provider talks to the Docker Engine API shape and maps data into the common runtime contracts. |
| `DockerApiProvider` | Explicit opt-in | TCP / HTTP(S) | Docker API compatible runtimes | TCP Docker API access grants runtime control. The app only enables TCP hosts when `AllowTcpDockerApi` is true. |
| Future `WslcSdkProvider` | Planned | WSLC SDK session | SDK-managed resources | This is intentionally separate from `wslc-cli` because current SDK sessions do not manage the same resource set as the CLI. |

## Selection Rules

- `wslc-cli` is the default provider and remains active when no external provider is configured.
- `DockerApiProvider` is selected only when the saved runtime provider is `docker-api` and a host is configured.
- Invalid Docker API settings do not prevent the app from starting; provider preview reports the error and default WSLC management remains available.
- Resources created through `DockerApiProvider` are labeled with `com.wslc-desktop.provider=docker-api` so future migration and filtering can distinguish external-runtime resources from WSLC CLI resources.

## Preview Behavior

- Settings calls native `POST /v1/providers/preview` as a read-only connection test.
- Named pipe hosts are allowed without additional TCP confirmation.
- TCP, HTTP, and HTTPS hosts require explicit `AllowTcpDockerApi` consent before the provider can connect.
- Without a configured Docker API host, preview returns unavailable and the GUI keeps using the default WSLC provider.

## Current Capability Shape

| Capability | `wslc-cli` | `DockerApiProvider` |
| --- | --- | --- |
| Container list/inspect/create/start/stop/remove | Supported | Supported |
| Image list/pull/remove | Supported | Supported |
| Named volumes | Supported | Supported |
| Bind mounts | Supported | Supported |
| Logs | Buffered/follow where WSLC supports it | Buffered/follow through Docker API logs |
| Exec | Non-interactive and basic TTY flows | Non-interactive and basic TTY flows |
| Stats | Snapshot and streaming contract | Snapshot contract, streaming endpoint shape retained |
| Networks | WSLC published-port view | Partial; Docker networks are not yet projected into the app endpoint table |
| Image build/push | Not yet supported | Not yet supported |
