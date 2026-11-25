param(
    [string]$MakeNsis = (Join-Path ${env:ProgramFiles(x86)} 'NSIS/Bin/makensis.exe')
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'PolarUninstallerWpf.csproj'
$props = Join-Path $PSScriptRoot 'Directory.Build.props'
$version = ([xml](Get-Content -LiteralPath $props -Raw)).Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'The project version must have three numeric components.' }
if (!(Test-Path -LiteralPath $MakeNsis -PathType Leaf)) { throw "NSIS not found: $MakeNsis" }

$buildId = [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff')
$output = Join-Path $PSScriptRoot "Build/TestBuilds/v$version/$buildId"
$stage = (New-Item -ItemType Directory -Path (Join-Path ([IO.Path]::GetTempPath()) ('polar-package-' + [guid]::NewGuid().ToString('N')))).FullName
$exe = Join-Path $stage 'PolarUninstaller.exe'
$setup = Join-Path $stage "PolarUninstaller-Setup-v$version.exe"
$sums = Join-Path $stage 'SHA256SUMS.txt'
try {
    & dotnet publish $project -c Release -p:PublishProfile=WindowsSingleFile -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'The single-file build failed.' }

    $installer = Join-Path $PSScriptRoot 'Installer-NSIS/PolarUninstaller.nsi'
    & $MakeNsis /V2 "/DVERSION=$version" "/DOUTFILE=$setup" "/DAPPEXE=$exe" $installer
    if ($LASTEXITCODE -ne 0) { throw 'The Setup build failed.' }

    $binaries = @(Get-Item -LiteralPath $exe, $setup)
    $checksums = foreach ($file in $binaries) {
        if ($file.VersionInfo.FileVersion -ne "$version.0") { throw "Invalid version: $($file.Name)" }
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($file.Name)"
    }
    Set-Content -LiteralPath $sums -Value $checksums -Encoding ascii

    New-Item -ItemType Directory -Path $output | Out-Null
    Copy-Item -LiteralPath $exe, $setup, $sums -Destination $output
    foreach ($file in $binaries) {
        $copied = Join-Path $output $file.Name
        $hash = (Get-FileHash -LiteralPath $copied -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($checksums -notcontains "$hash  $($file.Name)") { throw "Checksum mismatch: $copied" }
    }
    Get-ChildItem -LiteralPath $output -File | Select-Object FullName, Length
    Write-Output "Verified test package: $output"
}
finally {
    foreach ($file in @($exe, $setup, $sums)) {
        if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file }
    }
    if (!(Get-ChildItem -LiteralPath $stage -Force)) { [IO.Directory]::Delete($stage) }
}
