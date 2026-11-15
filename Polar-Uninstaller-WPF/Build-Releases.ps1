param(
    [string]$MigrationVersion = '1.0.129',
    [string]$UpdateVersion = '1.0.130',
    [string]$MakeNsis = 'C:\Program Files (x86)\NSIS\makensis.exe'
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_HOME = $PSScriptRoot
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.nuget'

function Set-SourceVersion([string]$Version) {
    $propsPath = Join-Path $PSScriptRoot 'Directory.Build.props'
    $props = [IO.File]::ReadAllText($propsPath)
    $props = [regex]::Replace($props, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
    [IO.File]::WriteAllText($propsPath, $props)

    $manifestPath = Join-Path $PSScriptRoot 'app.manifest'
    $manifest = [IO.File]::ReadAllText($manifestPath)
    $manifest = [regex]::Replace($manifest, '<assemblyIdentity version="[^"]*"', "<assemblyIdentity version=`"$Version.0`"")
    [IO.File]::WriteAllText($manifestPath, $manifest)
}

function Clear-BuildOutput {
    # The generated XAML resource URIs include the assembly version, so a clean
    # intermediate/output tree is required when switching versions.
    foreach ($relative in @('obj/Release', 'bin/Release')) {
        $path = Join-Path $PSScriptRoot $relative
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
}

function Publish-SingleFile([string]$Version) {
    Set-SourceVersion $Version
    Clear-BuildOutput
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build-SingleFile.ps1') 2>&1 | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "Single-file publish failed for $Version." }
    $exe = Join-Path $PSScriptRoot 'Build/SingleFile/PolarUninstaller.exe'
    if (!(Test-Path -LiteralPath $exe)) { throw "Single-file output is missing for $Version." }
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    if ($info.FileVersion -ne "$Version.0") { throw "Expected $Version.0 but found $($info.FileVersion)." }
    if ((Get-Item -LiteralPath $exe).Length -lt 50MB) { throw 'The single-file EXE is unexpectedly small; refusing to package it.' }
    return $exe
}

function Show-Artifact([string]$Path) {
    $item = Get-Item -LiteralPath $Path
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    [pscustomobject]@{
        Path = $item.FullName
        Bytes = $item.Length
        FileVersion = $info.FileVersion
        ProductVersion = $info.ProductVersion
        SHA256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    }
}

if (!(Test-Path -LiteralPath $MakeNsis)) { throw "NSIS was not found at $MakeNsis." }
$releases = Join-Path $PSScriptRoot 'Build/Releases'
New-Item -ItemType Directory -Force $releases | Out-Null

# --- Phase 1: one-time migration Setup -------------------------------------
$migrationExe = Publish-SingleFile $MigrationVersion
$migrationDir = Join-Path $releases "v$MigrationVersion"
New-Item -ItemType Directory -Force $migrationDir | Out-Null
$setup = Join-Path $migrationDir "PolarUninstaller-Setup-v$MigrationVersion.exe"
if (Test-Path -LiteralPath $setup) { Remove-Item -LiteralPath $setup -Force }

# NSIS does not reliably handle command-line defines whose paths contain spaces
# or non-ASCII characters. Stage the inputs under an ASCII temporary path and
# move the finished Setup into the release folder afterwards.
$nsisStage = Join-Path $env:TEMP ('polar-nsis-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $nsisStage | Out-Null
$nsisApp = Join-Path $nsisStage 'PolarUninstaller.exe'
$nsisOut = Join-Path $nsisStage "PolarUninstaller-Setup-v$MigrationVersion.exe"
Copy-Item -LiteralPath $migrationExe -Destination $nsisApp -Force
$nsi = Join-Path $PSScriptRoot 'Installer-NSIS/PolarUninstaller.nsi'
try {
    & $MakeNsis '/V2' "/DVERSION=$MigrationVersion" "/DOUTFILE=$nsisOut" "/DAPPEXE=$nsisApp" $nsi
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $nsisOut)) { throw 'The migration Setup was not produced.' }
    Move-Item -LiteralPath $nsisOut -Destination $setup -Force
}
finally {
    try { Remove-Item -LiteralPath $nsisStage -Recurse -Force } catch { }
}

# Keep a copy of the migration single-file for local migration tests.
$migrationStage = Join-Path $PSScriptRoot "obj/releases/v$MigrationVersion"
New-Item -ItemType Directory -Force $migrationStage | Out-Null
Copy-Item -LiteralPath $migrationExe -Destination (Join-Path $migrationStage 'PolarUninstaller.exe') -Force

# --- Phase 2: first EXE-only release ---------------------------------------
$updateExe = Publish-SingleFile $UpdateVersion
$updateDir = Join-Path $releases "v$UpdateVersion"
New-Item -ItemType Directory -Force $updateDir | Out-Null
$update = Join-Path $updateDir 'PolarUninstaller.exe'
Copy-Item -LiteralPath $updateExe -Destination $update -Force

# The source tree stays on the newest version.
Set-SourceVersion $UpdateVersion

Show-Artifact $setup | Format-List
Show-Artifact $update | Format-List
