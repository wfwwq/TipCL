# run-e2e.ps1 - build the launcher + this sample plugin, then run the net48 runtime E2E driver.
#
# ASCII-ONLY ON PURPOSE: Windows PowerShell 5.1 reads a BOM-less .ps1 as ANSI (cp936 here), so any
# non-ASCII literal in a BOM-less file fails to parse. Keep this file ASCII; put Chinese in the .md/.cs.
#
#   pwsh -NoProfile -File samples\dotnet-block-plugin\run-e2e.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File samples\dotnet-block-plugin\run-e2e.ps1
#
# NOTE (2026-11-14, independent acceptance finding): the temp-home parameter is
#   -E2eHome, NOT -Home. A parameter named Home collides with PowerShell's
#   read-only automatic variable $HOME, so binding used to throw before the script
#   body ever ran - which made this E2E unreproducible for any reader.
#
# Take the shared build lock first if other agents are working in this clone:
#   Set-Location 'D:\...\_tmp\locks'; powershell -NoProfile -File .\build-lock.ps1 -Name windos -Action Acquire -Owner <you>

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$E2eHome = '',
    [switch]$SkipLauncher
)

$ErrorActionPreference = 'Stop'
$Repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $Repo

if ([string]::IsNullOrWhiteSpace($E2eHome)) {
    $E2eHome = Join-Path $Repo '_wpf_run\native-block-e2e-home'
}

Write-Host "[1/4] build launcher (needed: the plugin references TipCL.exe)"
if (-not $SkipLauncher) {
    dotnet build 'src\TipCL\TipCL.csproj' -c $Configuration
    if ($LASTEXITCODE -ne 0) { Write-Host "launcher build FAILED ($LASTEXITCODE)"; exit $LASTEXITCODE }
}

Write-Host "[2/4] build sample plugin"
dotnet build 'samples\dotnet-block-plugin\DotNetBlockSample.csproj' -c $Configuration
if ($LASTEXITCODE -ne 0) { Write-Host "plugin build FAILED ($LASTEXITCODE)"; exit $LASTEXITCODE }

Write-Host "[3/4] build E2E driver (net48)"
dotnet build 'tests\TipCL.NativeBlockE2E\TipCL.NativeBlockE2E.csproj' -c $Configuration
if ($LASTEXITCODE -ne 0) { Write-Host "driver build FAILED ($LASTEXITCODE)"; exit $LASTEXITCODE }

$exe = Join-Path $Repo "tests\TipCL.NativeBlockE2E\bin\$Configuration\TipCL.NativeBlockE2E.exe"
$log = Join-Path $Repo "tests\TipCL.NativeBlockE2E\bin\$Configuration\e2e-output.txt"
Write-Host "[4/4] run E2E: $exe"
& $exe --home $E2eHome --out $log
$code = $LASTEXITCODE
Write-Host ""
Write-Host "driver exit code = $code   (0 = all assertions passed)"
Write-Host "temp TIPCL_HOME  = $E2eHome"
Write-Host "driver log       = $log"
exit $code
