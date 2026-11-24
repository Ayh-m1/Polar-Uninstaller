# Build and CI

Requirements: Windows and the .NET 9 SDK.

Run these commands from the repository root:

```powershell
dotnet run --project Polar-Uninstaller-WPF/Tests/CiSmoke/CiSmoke.csproj -c Release
dotnet restore Polar-Uninstaller-WPF/PolarUninstallerWpf.csproj -r win-x64 -p:PublishProfile=WindowsSingleFile
dotnet build Polar-Uninstaller-WPF/PolarUninstallerWpf.csproj -c Release -r win-x64 --no-restore
dotnet publish Polar-Uninstaller-WPF/PolarUninstallerWpf.csproj -c Release -p:PublishProfile=WindowsSingleFile --no-restore -o artifacts/SingleFile
./scripts/Test-PublishedBuild.ps1
```

The `Build` workflow in `.github/workflows/Build.yml` runs these checks on pushes
to `main`, `v*` tags, pull requests, and manual dispatches. It also builds the
versioned NSIS Setup and verifies both executable versions and SHA-256 checksums.
The installable and portable files, application license, and privacy policy are
available in the `PolarUninstaller-windows-x64` artifact. GitHub Releases are
published separately.

`Tests/CiSmoke` runs disposable filesystem fixtures for storage indexes, cache
updates and persistence, inventory metadata, settings, and duplicate detection.
It also checks that removal verification waits for the official process and
retries when registration updates are delayed.
The publish verifier checks the EXE size/version and tests the icon worker
protocol through the built DLL, without opening the elevated application UI.
Historical UI and launcher test harnesses are not part of this CI suite.

The application version comes from `Polar-Uninstaller-WPF/Directory.Build.props`.
Build outputs and local caches remain ignored by Git. NuGet access is required
to restore the self-contained Windows runtime packages on a fresh machine.
