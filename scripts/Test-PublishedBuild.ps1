param(
    [string]$PublishDirectory = 'artifacts/SingleFile',
    [string]$BuildAssembly = 'Polar-Uninstaller-WPF/bin/Release/net9.0-windows/PolarUninstaller.dll'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot
$exe = Get-Item -LiteralPath (Join-Path $PublishDirectory 'PolarUninstaller.exe')
$version = ([xml](Get-Content -Raw (Join-Path $repoRoot 'Polar-Uninstaller-WPF/Directory.Build.props'))).Project.PropertyGroup.Version
if ($exe.Length -lt 1MB) { throw 'The self-contained EXE is unexpectedly small.' }
if ($exe.VersionInfo.FileVersion -ne "$version.0") { throw 'The EXE version does not match Directory.Build.props.' }

# Exercise startup from the built assembly. The production EXE deliberately
# requires elevation, while the worker protocol itself needs no administrator rights.
$assembly = Get-Item -LiteralPath $BuildAssembly
$start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
$start.Arguments = '"' + $assembly.FullName + '" --polar-icon-worker'
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$process = [System.Diagnostics.Process]::Start($start)
try {
    $ready = $process.StandardOutput.ReadLineAsync()
    if (!$ready.Wait(30000)) { throw 'The published icon worker did not start within 30 seconds.' }
    if ($ready.Result -ne 'POLAR-ICON-READY') { throw 'The published icon worker handshake failed.' }
    $process.StandardInput.Close()
    if (!$process.WaitForExit(10000)) { throw 'The published icon worker did not exit after closing input.' }
    if ($process.ExitCode -ne 0) { throw "The published icon worker failed: $($process.StandardError.ReadToEnd())" }
} finally {
    if (!$process.HasExited) { $process.Kill() }
    $process.Dispose()
}

Copy-Item (Join-Path $repoRoot 'LICENSE') (Join-Path $PublishDirectory 'LICENSE.txt')
Copy-Item (Join-Path $repoRoot 'PRIVACY.md') (Join-Path $PublishDirectory 'PRIVACY.md')
$stream = [System.IO.File]::OpenRead($exe.FullName)
$sha = [System.Security.Cryptography.SHA256]::Create()
try { $hash = ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
finally { $stream.Dispose(); $sha.Dispose() }
"$hash  PolarUninstaller.exe" | Set-Content (Join-Path $PublishDirectory 'SHA256SUMS.txt') -Encoding utf8
Write-Output "Verified PolarUninstaller.exe v$version ($($exe.Length) bytes); built-assembly icon worker started and exited successfully."
