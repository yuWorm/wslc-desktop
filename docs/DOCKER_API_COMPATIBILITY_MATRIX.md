# Docker API Compatibility Matrix

This matrix tracks the Docker Engine API-compatible surface exposed by `wslcd` on `\\.\pipe\wslc-desktop-docker`.

Status values:

- `Not Started`: endpoint is planned but no implementation has been verified.
- `Partial`: endpoint is implemented with documented gaps and verifier coverage for the implemented subset.
- `Supported`: endpoint is implemented and has verifier coverage for the declared behavior.
- `Unsupported`: endpoint intentionally returns a Docker-style error because WSLC Desktop does not support the feature in the current product track.

No endpoint may be marked `Supported` without a verifier command listed in the `Verifier` column.

## Version And System

| Method | Endpoint | Target Phase | Status | WSLC Mapping | Unsupported Reason | Verifier |
| --- | --- | --- | --- | --- | --- | --- |
| GET | `/_ping` | 11 | Supported | Daemon health ping. | None. | `dotnet run --project tools\WslcdSmokeVerify\WslcdSmokeVerify.csproj --no-restore` |
| GET | `/version` | 11 | Supported | Product, daemon, provider, OS, arch, API version. | None. | `dotnet run --project tools\WslcdSmokeVerify\WslcdSmokeVerify.csproj --no-restore`; `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- docker-cli` |
| GET | `/v{version}/version` | 13 | Supported | Same as `/version` with version-prefix routing. | None. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe readonly` |
| GET | `/info` | 13 | Supported | WSLC host and provider capabilities. | Unsupported Docker fields are returned as neutral values or omitted only when Docker clients tolerate omission. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe readonly`; `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- docker-cli` |
| GET | `/v{version}/info` | 13 | Supported | Same as `/info` with version-prefix routing. | None. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe readonly`; `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- docker-cli` |

## Containers

| Method | Endpoint | Target Phase | Status | WSLC Mapping | Unsupported Reason | Verifier |
| --- | --- | --- | --- | --- | --- | --- |
| GET | `/containers/json` | 13 | Supported | `IContainerRuntimeProvider.ListContainers`; supports Docker status, name, id, ancestor, and label filters for the declared subset. | None. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe readonly`; `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- docker-cli` |
| GET | `/v{version}/containers/json` | 13 | Supported | Same as `/containers/json` with version-prefix routing. | None. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe readonly`; `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- docker-cli` |
| GET | `/containers/{id}/json` | 13 | Supported | `IContainerRuntimeProvider.InspectContainer`. | None. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe readonly` |
| GET | `/v{version}/containers/{id}/json` | 13 | Supported | Same as `/containers/{id}/json` with version-prefix routing. | None. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe readonly` |
| POST | `/containers/create` | 14 | Supported | `IContainerRuntimeProvider.CreateContainer`; maps image, name, command, entrypoint, environment, working directory, labels, binds, ports, restart policy, and auto-remove where supported. | None. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe lifecycle`; `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- docker-cli` |
| POST | `/v{version}/containers/create` | 14 | Supported | Same as `/containers/create` with version-prefix routing. | None. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe lifecycle`; `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- docker-cli` |
| POST | `/containers/{id}/start` | 14 | Supported | `IContainerRuntimeProvider.StartContainer`. | None. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe lifecycle`; `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- docker-cli` |
| POST | `/containers/{id}/stop` | 14 | Supported | `IContainerRuntimeProvider.StopContainer`. | None. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe lifecycle` |
| POST | `/containers/{id}/restart` | 14 | Supported | Provider restart or stop/start fallback. | None. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe lifecycle` |
| POST | `/containers/{id}/kill` | 14 | Partial | Provider default kill maps to stop; non-default signals return Docker-style unsupported error. | WSLC CLI does not expose arbitrary signal mapping. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe lifecycle` |
| DELETE | `/containers/{id}` | 14 | Supported | `IContainerRuntimeProvider.RemoveContainer`. | None. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe lifecycle`; `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- docker-cli` |
| GET | `/containers/{id}/logs` | 16 | Partial | `IContainerRuntimeProvider.StreamLogs`; non-TTY output is encoded as Docker raw-stream frames. | Buffered logs are implemented; `follow` is parsed but WSLC CLI provider currently reports no live follow capability. | `scripts\Verify-DockerApiCompat.ps1 -Mode streaming` |
| GET | `/containers/{id}/stats` | 16 | Partial | `IContainerRuntimeProvider.StreamStats`; returns Docker-shaped one-shot CPU and memory JSON for `stream=false`; default `stream=true` returns Docker-style 501 when the provider lacks `StatsStreaming`. | Live streaming is implemented only for providers that report `StatsStreaming`; WSLC CLI currently reports no streaming stats capability. | `scripts\Verify-DockerApiCompat.ps1 -Mode streaming` |

## Exec

| Method | Endpoint | Target Phase | Status | WSLC Mapping | Unsupported Reason | Verifier |
| --- | --- | --- | --- | --- | --- | --- |
| POST | `/containers/{id}/exec` | 16 | Partial | `IContainerRuntimeProvider.CreateExec`; stores a bounded non-interactive exec session. | Interactive TTY exec returns Docker-style 501 until a provider reports `ExecInteractiveTty`. | `scripts\Verify-DockerApiCompat.ps1 -Mode streaming` |
| POST | `/exec/{id}/start` | 16 | Partial | `IContainerRuntimeProvider.StartExec`; attached stdout/stderr are encoded as Docker raw-stream frames. Docker CLI upgrade requests are handled by `DockerPipeProxy`, which writes `101 Switching Protocols` and Docker raw-stream frames directly on the public named pipe. | Interactive TTY is unsupported for the WSLC CLI provider. | `scripts\Verify-DockerApiCompat.ps1 -Mode streaming`; `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- streaming`; `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- docker-cli` covers clean non-interactive Docker CLI exec. |
| GET | `/exec/{id}/json` | 16 | Partial | `IContainerRuntimeProvider.InspectExec`; retained sessions are pruned by age and max count. | Retention is in-memory for the daemon process. | `scripts\Verify-DockerApiCompat.ps1 -Mode streaming` |
| POST | `/exec/{id}/resize` | 16 | Partial | Non-interactive exec resize validates the exec id and returns no-content success. | Interactive TTY resize returns Docker-style 501 because the WSLC CLI provider has no TTY capability. | `scripts\Verify-DockerApiCompat.ps1 -Mode streaming` |

## Images

| Method | Endpoint | Target Phase | Status | WSLC Mapping | Unsupported Reason | Verifier |
| --- | --- | --- | --- | --- | --- | --- |
| GET | `/images/json` | 13 | Supported | `IContainerRuntimeProvider.ListImages`. | None. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe readonly` |
| GET | `/v{version}/images/json` | 13 | Supported | Same as `/images/json` with version-prefix routing. | None. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe readonly` |
| GET | `/images/{name}/json` | 13 | Supported | Provider image inspect or list-filter fallback, including namespaced references such as `traefik/whoami`. | Returns 404 when image details cannot be found. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe readonly`; `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- readonly` |
| POST | `/images/create` | 15 | Supported | `IContainerRuntimeProvider.PullImage`; streams newline-delimited Docker JSON progress messages. | None. | `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- resources`; `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- docker-cli` |
| DELETE | `/images/{name}` | 15 | Supported | `IContainerRuntimeProvider.RemoveImage`. | None. | `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- resources` |
| GET | `/images/search` | 15 | Unsupported | No WSLC local-runtime equivalent. | Registry search is outside the accepted daemon MVP until a registry provider is designed. | `tools\DockerApiCompatVerify -- resources` |
| POST | `/build` | 20 | Unsupported | No build provider accepted in this track. | Image build belongs to a later registry/build workflow. | `tools\DockerApiCompatVerify -- unsupported` |

## Volumes

| Method | Endpoint | Target Phase | Status | WSLC Mapping | Unsupported Reason | Verifier |
| --- | --- | --- | --- | --- | --- | --- |
| GET | `/volumes` | 15 | Supported | `IContainerRuntimeProvider.ListVolumes`. | None. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe resources`; `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- docker-cli` |
| POST | `/volumes/create` | 15 | Supported | `IContainerRuntimeProvider.CreateVolume`, including Compose labels where provided. | None when provider reports `NamedVolumes`; otherwise unsupported. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe resources`; `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- docker-cli` |
| GET | `/volumes/{name}` | 15 | Supported | Provider volume detail from list/filter. | None. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe resources` |
| DELETE | `/volumes/{name}` | 15 | Supported | `IContainerRuntimeProvider.RemoveVolume`. | None when provider reports `NamedVolumes`; otherwise unsupported. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe resources` |

## Networks

| Method | Endpoint | Target Phase | Status | WSLC Mapping | Unsupported Reason | Verifier |
| --- | --- | --- | --- | --- | --- | --- |
| GET | `/networks` | 15 | Supported | Provider network summaries or published-port inventory; label-filtered Compose project network discovery does not claim the unmanaged default WSLC network. | None. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe resources`; `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- docker-cli` |
| GET | `/networks/{id}` | 15 | Supported | Provider network detail or default WSLC network representation. | None. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe resources` |
| POST | `/networks/create` | 15 | Unsupported | No confirmed WSLC user-defined network create operation. | Returns Docker-style unsupported error until WSLC exposes network CRUD. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe resources` |
| DELETE | `/networks/{id}` | 15 | Unsupported | No confirmed WSLC user-defined network delete operation. | Returns Docker-style unsupported error for non-managed/default networks. | `artifacts\DockerApiCompatVerify-singlefile\DockerApiCompatVerify.exe resources` |

## Compose-Driven Coverage

Compose uses Docker Engine API endpoints rather than a separate daemon protocol. The practical first target is:

| Workflow | Target Phase | Required Endpoints | Status | Verifier |
| --- | --- | --- | --- | --- |
| `docker --context wslc-desktop ps -a` | 17 | `/containers/json`, `/version`, `/_ping` | Supported | `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- docker-cli` |
| `docker --context wslc-desktop compose config` | 17 | Local compose parsing by Docker CLI; no daemon write required. | Supported | `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- docker-cli`; standalone `docker-compose.exe` fallback is supported. |
| `docker --context wslc-desktop compose up -d` | 17 | image create, container create/start, default networking, volumes, labels, and logs as needed by the fixture. | Supported | `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- docker-cli`; fixture uses `network_mode: default` to avoid unsupported network CRUD. |
| `docker --context wslc-desktop compose down` | 17 | container stop/remove and volume remove when requested. | Supported | `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- docker-cli`; cleanup path removes Compose-created containers and volumes. |

## Marking Rules

- Update this file in the same phase that changes endpoint behavior.
- If an endpoint remains intentionally unsupported, set status to `Unsupported` and keep the user-visible reason stable.
- If a verifier covers only a subset, set status to `Partial` and describe the missing behavior in the `Unsupported Reason` column.
- Do not mark an endpoint `Supported` until the verifier listed in the row has fresh passing output.
