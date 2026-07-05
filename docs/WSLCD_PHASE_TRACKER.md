# WSLCd Phase Tracker

This document is the coarse-grained, markable direction contract for the daemon-backed WSLC Desktop track.

Detailed task steps live in [WSLCd Docker API Compatibility Implementation Plan](superpowers/plans/2026-07-05-wslcd-docker-api-compatibility.md). Current evidence lives in [PHASE_STATUS.md](PHASE_STATUS.md). This file is the lightweight progress board used to decide what can be worked on next.

## Marking Rules

- Mark a phase complete only when its `Phase complete` checkbox and every exit gate checkbox are checked.
- If a gate is blocked by the local machine, preview WSLC behavior, Docker CLI absence, or Windows App Control, leave it unchecked and record the blocker under `Blockers`.
- Do not start a later phase for production behavior until the current phase is complete, unless the skipped gate is explicitly recorded as a non-product blocker.
- The GUI must use the same daemon/runtime path as Docker-compatible clients after Phase 18. No new WinUI feature should call `wslc.exe` directly after that phase is complete.
- Unsupported Docker API behavior must be explicit, documented, and covered by a verifier or source check before it is accepted.

## Track Scope

The previous native WinUI MVP track is complete:

- [x] Phase 0 - Foundation
- [x] Phase 1 - Product Shell
- [x] Phase 2 - Host and Session
- [x] Phase 3 - Images
- [x] Phase 4 - Containers
- [x] Phase 5 - Logs and Terminal
- [x] Phase 6 - Volumes and Networking
- [x] Phase 7 - Compose Preview
- [x] Phase 8 - Hardening and Packaging
- [x] Phase 9 - Expansion Decisions

The active track is the daemon-backed WSLCd and Docker API compatibility path:

- [x] Phase 10 - Architecture Lock
- [x] Phase 11 - Daemon Foundation
- [x] Phase 12 - Native Runtime API
- [x] Phase 13 - Docker API Read-Only MVP
- [x] Phase 14 - Docker Container Lifecycle
- [x] Phase 15 - Image, Volume, And Network APIs
- [x] Phase 16 - Logs, Stats, And Exec Streaming
- [x] Phase 17 - Docker Context And Compose Compatibility
- [x] Phase 18 - GUI As Daemon Client
- [x] Phase 19 - External Runtime Providers
- [x] Phase 20 - Packaging, Security, And Release Gates

## Phase 10 - Architecture Lock

**Goal:** Freeze the daemon/provider/API contract before implementation continues.

- [x] Phase complete
- [x] Document `wslcd`, WinUI client, native API, Docker API endpoint, runtime providers, and verifier tools.
- [x] Lock default named pipes: `\\.\pipe\wslc-desktop` and `\\.\pipe\wslc-desktop-docker`.
- [x] Lock default Docker context name: `wslc-desktop`.
- [x] Define runtime provider contract and capability flags.
- [x] Create Docker API compatibility matrix.
- [x] Remove old product direction that treated Docker API compatibility as deferred or discouraged.
- [x] `dotnet build --no-restore` passes.

**Blockers:** None.

## Phase 11 - Daemon Foundation

**Goal:** Create a user-mode daemon that can be started, health-checked, stopped, and diagnosed.

- [x] Phase complete
- [x] Add `src/wslcd`.
- [x] Add shared contracts project.
- [x] Host native health/version endpoints on the native named pipe.
- [x] Host Docker `_ping` and `/version` endpoints on the Docker named pipe.
- [x] Write daemon diagnostics under the app diagnostics location.
- [x] Add smoke verifier that starts and stops only the daemon it owns.
- [x] Smoke verifier passes without orphaning `wslcd`.

**Blockers:** None.

## Phase 12 - Native Runtime API

**Goal:** Make `wslcd` own WSLC runtime operations through a provider boundary.

- [x] Phase complete
- [x] Add provider-neutral runtime contracts.
- [x] Add the default `WslcCliProvider`.
- [x] Expose native API routes for runtime, containers, images, volumes, networks, and operations.
- [x] Track mutating operations with stdout/stderr tails and exit state.
- [x] Return native error envelopes with provider command details.
- [x] Verifier confirms native API resources match `wslc.exe`.

**Blockers:** None.

## Phase 13 - Docker API Read-Only MVP

**Goal:** Let Docker-compatible clients read WSLC state through the `wslcd` Docker pipe.

- [x] Phase complete
- [x] Support Docker API version-prefix routing.
- [x] Return Docker-style error envelopes.
- [x] Implement `_ping`, `/version`, `/info`.
- [x] Implement read-only container list/inspect.
- [x] Implement read-only image list/inspect.
- [x] Direct named-pipe verifier passes.
- [x] Docker CLI `version` and `info` pass against `npipe:////./pipe/wslc-desktop-docker`.
- [x] Docker CLI `ps -a` and `images` match `wslc` resource output.

**Blockers:** None.

## Phase 14 - Docker Container Lifecycle

**Goal:** Support Docker-compatible create/start/stop/restart/remove for WSLC containers.

- [x] Phase complete
- [x] Implement Docker-compatible container create.
- [x] Map image, name, command, entrypoint, environment, working directory, labels, binds, ports, restart policy, and auto-remove where supported.
- [x] Validate Windows bind mount syntax correctly.
- [x] Implement start, stop, restart, kill, and remove routes.
- [x] Return Docker-style conflict, missing image, invalid mount, invalid port, and state errors.
- [x] Direct lifecycle verifier passes.
- [x] Docker CLI create/start/remove smoke passes.

**Blockers:** None.

## Phase 15 - Image, Volume, And Network APIs

**Goal:** Cover Docker resource APIs needed by GUI workflows, Docker CLI basics, and Compose setup.

- [x] Phase complete
- [x] Implement image pull through `POST /images/create`.
- [x] Implement image delete route.
- [x] Cover image delete with a non-destructive verifier fixture.
- [x] Decide whether `/images/search` is explicitly unsupported or backed by a registry search provider.
- [x] Implement volume list/create/inspect/delete.
- [x] Implement network list/inspect.
- [x] Return explicit unsupported errors for network create/delete.
- [x] Direct resources verifier passes.
- [x] Docker CLI image pull and volume list smoke passes.

**Blockers:** None.

## Phase 16 - Logs, Stats, And Exec Streaming

**Goal:** Make logs, stats, and exec usable through Docker-compatible clients and IDE integrations.

- [x] Phase complete
- [x] Implement container logs with query parsing.
- [x] Implement Docker raw-stream framing for non-TTY output.
- [x] Implement one-shot stats response.
- [x] Return explicit unsupported for true stats streaming when the active provider lacks it.
- [x] Implement exec create/start/inspect/resize.
- [x] Return explicit unsupported for interactive TTY exec when the active provider lacks it.
- [x] Bound exec session retention.
- [x] PowerShell/direct streaming verifier passes.
- [x] C# verifier mode passes without Windows App Control blocking it.
- [x] Docker CLI logs/stats/exec smoke passes.

**Blockers:** None.

**Current evidence:**

- `DockerPipeProxy` owns the public Docker named pipe and forwards normal HTTP traffic to an internal Kestrel pipe, while directly handling Docker CLI exec-start hijack requests.
- `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- streaming` passed on 2026-07-05 with `DOCKER_API_STREAMING_OK`.
- `dotnet run --project tools\DockerApiCompatVerify\DockerApiCompatVerify.csproj --no-restore -- docker-cli` passed on 2026-07-05 with `DOCKER_API_DOCKER_CLI_OK`, covering Docker CLI logs, `stats --no-stream`, and non-interactive exec against the Release AppX packaged daemon.

## Phase 17 - Docker Context And Compose Compatibility

**Goal:** Make normal developer workflows target `wslcd` through a Docker context and a practical Compose subset.

- [x] Phase complete
- [x] Document Docker context setup.
- [x] Add context validation mode to the Docker API verifier.
- [x] Document supported, partial, and rejected Compose service keys.
- [x] Add a smoke Compose fixture.
- [x] Add Compose cleanup verification logic.
- [x] Source verifier passes.
- [x] `docker context inspect wslc-desktop` validates the named-pipe host.
- [x] `docker --context wslc-desktop ps -a` passes.
- [x] `docker --context wslc-desktop compose config` passes.
- [x] `docker --context wslc-desktop compose up -d` creates visible WSLC containers or returns a documented endpoint-level unsupported error.
- [x] `docker --context wslc-desktop compose down` removes created resources.

**Blockers:** None.

## Phase 18 - GUI As Daemon Client

**Goal:** Move the WinUI app onto the same daemon truth path as Docker-compatible clients.

- [x] Phase complete
- [x] Add `src/WslcDesktop.DaemonClient` with typed native API methods for health, containers, images, volumes, networks, logs, exec, operations, and diagnostics.
- [x] Replace direct service wiring in `Services/AppServices.cs` with daemon-backed services.
- [x] Keep mock daemon behavior for design-time and unsupported-host states.
- [x] Add daemon startup orchestration when the GUI opens.
- [x] Add graceful disconnect and restart behavior when the daemon stops.
- [x] Add operation progress polling or streaming through the native API.
- [x] Update Dashboard, Containers, Images, Volumes, Networks, Compose, and Settings ViewModels to consume daemon-backed services.
- [x] Remove direct `wslc.exe` process execution from the WinUI runtime path after parity is verified.
- [x] Keep existing localization keys stable unless new daemon/client errors require additions.
- [x] `dotnet build --no-restore` passes.
- [x] Localization, Compose plan, and Hardening verifiers pass.
- [x] Launched GUI shows the same containers and images as `GET /v1/containers` and `GET /v1/images`.
- [x] GUI lifecycle actions are reflected through the Docker pipe.

**Blockers:** None.

## Phase 19 - External Runtime Providers

**Goal:** Add Docker API-compatible runtime providers after the WSLC provider path is stable.

- [x] Phase complete
- [x] Add `DockerApiProvider` behind `IContainerRuntimeProvider`.
- [x] Support explicit Windows named-pipe Docker hosts.
- [x] Support TCP Docker hosts only after user confirmation and security warning.
- [x] Add provider selection in Settings.
- [x] Add provider capability preview before activation.
- [x] Add read-only connection test.
- [x] Keep WSLC-created and external-engine resources distinguishable.
- [x] Document provider support in `docs/RUNTIME_PROVIDER_MATRIX.md`.
- [x] Provider verifier passes when a configured Docker API host is available.
- [x] Without a configured external host, Settings shows a clear unavailable state and keeps WSLC active.

**Blockers:** None.

**Current evidence:**

- `WSLCD_DOCKER_API_HOST=npipe:////./pipe/wslc-desktop-docker` plus `WSLCD_SMOKE_DAEMON_PATH=<Release AppX wslcd.exe>` ran `dotnet run --project tools\WslcdSmokeVerify\WslcdSmokeVerify.csproj --no-restore -- provider docker-api` and printed `WSLCD_DOCKER_PROVIDER_OK`.
- The provider smoke starts a second daemon with `WSLCD_RUNTIME_PROVIDER=docker-api`, points it at the first daemon's Docker API pipe, verifies `GET /v1/runtime` reports `docker-api`, checks image/container parity against the default WSLC provider, and then verifies the default WSLC provider still matches `wslc.exe`.

## Phase 20 - Packaging, Security, And Release Gates

**Goal:** Ship the daemon-backed product with a clear support boundary and repeatable verification.

- [x] Phase complete
- [x] Start `wslcd` as a current-user process from the packaged WinUI app.
- [x] Add optional launch-at-login controls.
- [x] Restrict both named pipes to the current Windows user by default.
- [x] Export diagnostics for daemon logs, provider command history, compatibility matrix version, and pipe health.
- [x] Add daemon crash recovery flow.
- [x] Add release notes for Docker API coverage, unsupported endpoints, security model, and known WSLC limitations.
- [x] Verify x64 and ARM64 packages.
- [x] Add a full verification script for build, daemon smoke, Docker API smoke, localization, compose plan, hardening, and package creation.
- [x] Packaged app launches the daemon and lists WSLC resources.
- [x] Docker pipe smoke passes against the packaged daemon.
- [x] Compatibility matrix has no `Supported` endpoint without verifier coverage.

**Blockers:** None.

**Current evidence:**

- `powershell -ExecutionPolicy Bypass -File .\scripts\Verify-FullRelease.ps1 -SkipPackage` passed and printed `WSLC_FULL_RELEASE_VERIFY_OK`.
- The full release verifier produced `WSLCD_FULL_SMOKE_OK` and `DOCKER_API_COMPAT_FULL_OK` against the Release AppX packaged daemon, after source gates, compatibility matrix validation, localization, compose plan, hardening, Debug build, and Release build all passed.
