# WSLC Desktop Release Notes

## Docker API coverage

WSLC Desktop ships a local `wslcd` daemon with a Docker Engine API-compatible named-pipe endpoint at `npipe:////./pipe/wslc-desktop-docker`.

Covered areas:

- Read-only daemon, version, info, container list/inspect, and image list/inspect endpoints.
- Container create, start, stop, restart, kill, remove, logs, stats snapshot, exec create/start/inspect/resize.
- Image pull and remove.
- Volume list/create/inspect/remove.
- Default WSLC network list/inspect.
- Docker context and Compose compatibility for the supported subset.

## Unsupported behavior

Unsupported or partial behaviors are intentionally explicit:

- Docker CLI and Compose release gates require Docker CLI to be installed locally.
- Full Docker network create/delete is not supported until WSLC exposes compatible user-defined network primitives.
- Image build and image push are not supported in this release.
- Registry login and credential storage are not included.
- Interactive TTY exec remains limited by provider capability.
- Docker API TCP exposure is not enabled for the `wslcd` daemon.

The endpoint-by-endpoint source of truth is `docs/DOCKER_API_COMPATIBILITY_MATRIX.md`.

## Security model

- `wslcd` is a current-user process started by the WinUI app.
- Native and Docker-compatible endpoints are Windows named pipes.
- Kestrel named-pipe transport is configured with `CurrentUserOnly = true`.
- Docker API-compatible pipe access is treated as runtime control. Do not expose the pipe to other users or unauthenticated TCP listeners.
- External Docker API providers require explicit user configuration. TCP/HTTP(S) provider hosts require the user-facing `AllowTcpDockerApi` confirmation.
- Startup-at-login uses the packaged Windows StartupTask API and remains opt-in.

## Known limitations

- Docker CLI read-only, lifecycle, image pull, volume list, context, and Compose gates passed against the packaged daemon on the current development machine.
- Docker CLI logs, `stats --no-stream`, and non-interactive exec gates pass against the packaged daemon. Interactive TTY exec remains limited by provider capability.
- Windows App Control can block rebuilt Debug verifier and daemon dependency DLLs with `0x800711C7` on this machine. Release verification uses the packaged AppX `wslcd.exe` path through `WSLCD_SMOKE_DAEMON_PATH`, which passed the full daemon and Docker API gates.
- `Microsoft.WSL.Containers` SDK sessions are not the default provider because current observed SDK session resources do not match the user-visible `wslc.exe` resource set.
- Packaged daemon verification requires the MSIX layout to include `wslcd.exe`, `wslcd.dll`, and daemon dependency DLLs beside the WinUI executable.
- Compatibility matrix metadata is embedded in diagnostics when the repository docs are available; packaged layouts may report the matrix file as unavailable.
