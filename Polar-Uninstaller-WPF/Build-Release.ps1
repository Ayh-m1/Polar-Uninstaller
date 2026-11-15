param([string]$DotNetRoot='C:\Program Files\dotnet')
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_HOME=$PSScriptRoot
$env:NUGET_PACKAGES=Join-Path $PSScriptRoot '.nuget'
$dotnet=Join-Path $DotNetRoot 'dotnet.exe'
if(!(Test-Path -LiteralPath $dotnet)){throw 'The .NET SDK was not found.'}
$stage=Join-Path $PSScriptRoot 'obj/production-publish'
if(Test-Path -LiteralPath $stage){
 $resolved=(Resolve-Path -LiteralPath $stage).Path
 if($resolved -ne (Join-Path $PSScriptRoot 'obj\production-publish')){throw 'Unexpected staging path'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
& $dotnet publish PolarUninstallerWpf.csproj -c Release --no-restore --self-contained false -o $stage -p:DebugType=None -p:DebugSymbols=false -p:AppHostDotNetSearch=AppRelative -p:AppHostRelativeDotNet=.runtime
if($LASTEXITCODE -ne 0){throw 'Release publish failed. The existing Build was not replaced.'}
$runtimeVersion=Get-ChildItem -LiteralPath (Join-Path $DotNetRoot 'shared/Microsoft.WindowsDesktop.App') -Directory | Where-Object { $_.Name -match '^9\.0\.\d+$' -and (Test-Path -LiteralPath (Join-Path $DotNetRoot ('shared/Microsoft.NETCore.App/'+$_.Name))) -and (Test-Path -LiteralPath (Join-Path $DotNetRoot ('host/fxr/'+$_.Name))) } | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1 -ExpandProperty Name
if(!$runtimeVersion){throw 'Matching Windows Desktop, .NET Core and hostfxr 9.0 runtime files were not found.'}
foreach($folder in @('shared/Microsoft.NETCore.App','shared/Microsoft.WindowsDesktop.App','host/fxr')){
 $destination=Join-Path $stage ('.runtime/'+$folder+'/'+$runtimeVersion)
 New-Item -ItemType Directory -Force $destination|Out-Null
 Copy-Item -Path (Join-Path $DotNetRoot ($folder+'/'+$runtimeVersion+'/*')) -Destination $destination -Recurse
}
foreach($name in @('LICENSE.txt','ThirdPartyNotices.txt')){if(Test-Path -LiteralPath (Join-Path $DotNetRoot $name)){Copy-Item -LiteralPath (Join-Path $DotNetRoot $name) -Destination (Join-Path $stage '.runtime')}}
$version=([xml][IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Directory.Build.props'))).Project.PropertyGroup.Version
$manifest=Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object {[ordered]@{path=[IO.Path]::GetRelativePath($stage,$_.FullName);bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}}
[IO.File]::WriteAllText((Join-Path $stage 'release-files.json'),([ordered]@{product='Polar Uninstaller';version=$version;runtime=$runtimeVersion;files=@($manifest)}|ConvertTo-Json -Depth 6))
if((Get-Item -LiteralPath (Join-Path $stage 'PolarUninstaller.exe')).VersionInfo.FileVersion -ne "$version.0"){throw 'Executable version mismatch'}
$output=Join-Path $PSScriptRoot 'Build';New-Item -ItemType Directory -Force $output|Out-Null
# Build contains only final artifacts; obj holds all temporary publishing files.
Get-ChildItem -LiteralPath $stage -Force | Copy-Item -Destination $output -Recurse -Force
Add-Type -AssemblyName System.IO.Compression
$zip=Join-Path $output "PolarUninstaller-v$version-portable.zip"
$temporaryZip=Join-Path $PSScriptRoot 'obj/production-portable.zip'
if(Test-Path -LiteralPath $temporaryZip){Remove-Item -LiteralPath $temporaryZip}
$archive=[IO.Compression.ZipFile]::Open($temporaryZip,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($file in Get-ChildItem -LiteralPath $stage -File -Recurse){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,[IO.Path]::GetRelativePath($stage,$file.FullName).Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal)|Out-Null}}finally{$archive.Dispose()}
Move-Item -LiteralPath $temporaryZip -Destination $zip -Force
Write-Output "Release EXE: $(Join-Path $output 'PolarUninstaller.exe')"
Write-Output "Portable ZIP: $zip"
Write-Output "Private runtime: $runtimeVersion (no runtime installation or TEMP/TMP modification)"
