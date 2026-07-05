# Development Workflow

The app keeps daily development and release packaging separate.

## Daily Development

Use the signed packaged debug path for normal local work:

```powershell
.\scripts\Start-Development.ps1
```

The script builds the app, signs the loose AppX layout, and starts it with `winapp run`. This avoids Windows Application Control, WDAC, or Smart App Control blocking local project DLLs.

The raw `dotnet run` path is not the preferred development command for this project. The WinApp NuGet `dotnet run` integration prepares a loose AppX layout internally and does not provide a reliable project hook for signing the final payload before launch.

```powershell
dotnet run --launch-profile "wslc-desktop (Package)"
```

If this is the first run on a machine, trust the repo development certificate for the current Windows user:

```powershell
.\scripts\Install-DevelopmentCertificate.ps1
```

The certificate is imported into `CurrentUser\Root` and `CurrentUser\TrustedPublisher`.

## Packaged Debug

Use packaged debug only when you need package identity behavior, manifest behavior, startup task behavior, or MSIX-specific diagnostics:

```powershell
.\scripts\Start-PackagedDebug.ps1
```

The packaged debug script builds the app, locates the generated AppX payload, and launches it with:

```powershell
winapp run --debug-output
```

The script signs the loose AppX payload before launch. Without signing and a trusted development certificate, a typical failure looks like `0x800711C7` and can happen before the first window appears.

The `wslc-desktop (Unpackaged)` profile is retained for investigation only. On this WinUI/MSIX project it is not the default development path because raw unpackaged execution can fail before the first window appears.

## Release Packaging

Do not add `<WindowsPackageType>None</WindowsPackageType>` to work around packaged launch issues. Keep `Package.appxmanifest` and the MSIX tooling enabled for release validation.

Before release-oriented changes, run:

```powershell
.\scripts\Verify-FullRelease.ps1
```
