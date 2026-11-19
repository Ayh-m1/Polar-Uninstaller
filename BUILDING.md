# Build and CI

Requirements: Windows and the .NET 9 SDK.

Run these commands from the repository root:

```powershell
dotnet restore Polar-Uninstaller-WPF/PolarUninstallerWpf.csproj
dotnet build Polar-Uninstaller-WPF/PolarUninstallerWpf.csproj -c Release --no-restore
dotnet run --project Polar-Uninstaller-WPF/Tests/CiSmoke/CiSmoke.csproj -c Release
dotnet publish Polar-Uninstaller-WPF/PolarUninstallerWpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/SingleFile
./scripts/Test-PublishedBuild.ps1
```

The `Build` workflow in `.github/workflows/Build.yml` runs these checks on pushes
to `main`, `v*` tags, pull requests, and manual dispatches. It uploads the EXE,
font licenses, application license, privacy policy, and SHA-256 checksum as the
`PolarUninstaller-exe` artifact. This workflow does not publish a GitHub Release.

`Tests/CiSmoke` runs disposable filesystem fixtures for storage indexes, cache
updates and persistence, inventory metadata, settings, and duplicate detection.
The publish verifier checks the EXE size/version and tests the icon worker
protocol through the built DLL, without opening the elevated application UI.
Historical UI and launcher test harnesses are not part of this CI suite.

The application version comes from `Polar-Uninstaller-WPF/Directory.Build.props`.
Build outputs and local caches remain ignored by Git. NuGet access is required
to restore the self-contained Windows runtime packages on a fresh machine.
