param([string]$DotNet='dotnet')
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_HOME=$PSScriptRoot
$env:NUGET_PACKAGES=Join-Path $PSScriptRoot '.nuget'
$stage=Join-Path $PSScriptRoot 'obj/single-file-publish'
if(Test-Path -LiteralPath $stage){
 $resolved=(Resolve-Path -LiteralPath $stage).Path
 if($resolved -ne (Join-Path $PSScriptRoot 'obj\single-file-publish')){throw 'Unexpected single-file staging directory'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
& $DotNet publish PolarUninstallerWpf.csproj -p:PublishProfile=WindowsSingleFile -o $stage
if($LASTEXITCODE-ne 0){throw 'Single-file publish failed; existing Release output was preserved.'}
$files=@(Get-ChildItem -LiteralPath $stage -Force)
if($files.Count-ne 1-or $files[0].Name-ne 'PolarUninstaller.exe'){throw 'Publish output is not a single standalone EXE.'}
$version=([xml][IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Directory.Build.props'))).Project.PropertyGroup.Version
if($files[0].VersionInfo.FileVersion -ne "$version.0"){throw 'Executable version mismatch'}
$output=Join-Path $PSScriptRoot 'Build/SingleFile';New-Item -ItemType Directory -Force $output|Out-Null
if(@(Get-ChildItem -LiteralPath $output -Force|Where-Object Name -ne 'PolarUninstaller.exe').Count){throw 'SingleFile output contains unexpected files; it was not modified.'}
Copy-Item -LiteralPath $files[0].FullName -Destination (Join-Path $output 'PolarUninstaller.exe') -Force
Get-Item -LiteralPath (Join-Path $output 'PolarUninstaller.exe')|Select-Object FullName,Length
Get-FileHash -LiteralPath (Join-Path $output 'PolarUninstaller.exe') -Algorithm SHA256
