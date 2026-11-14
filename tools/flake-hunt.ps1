# flake-hunt.ps1 -- run N rounds of the atomic four-run (build + VSTest + in-proc) inside ONE lock hold each,
# printing one block per round. Acceptance ("both channels green N rounds in a row") becomes one command.
#
# ASCII ONLY in this file, and **only ASCII match patterns** on purpose:
#   * PowerShell 5.1 reads a BOM-less .ps1 as ANSI, so Chinese literals would be mangled (D-36);
#   * the logs are Chinese, so we never match on their words -- we match "[FAIL]" / ": error " / ": warning "
#     (all ASCII) and print the summary file and both log tails VERBATIM.
#
# USAGE (never wrap this in another lock: it takes the build lock itself for every round):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\flake-hunt.ps1 -Rounds 3 -Prefix d1c

[CmdletBinding()]
param(
    [int]$Rounds = 3,
    [string]$Prefix = 'flake',
    [int]$TimeoutSec = 2400
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

for ($r = 1; $r -le $Rounds; $r++) {
    $name = 'artifacts\' + $Prefix + '-r' + $r
    $what = 'artifacts\lead-verify.cmd ' + $name

    powershell -NoProfile -ExecutionPolicy Bypass -File tools\with-build-lock.ps1 -What $what -TimeoutSec $TimeoutSec | Out-Null

    $summaryPath = $name + '-summary.txt'
    $buildPath = $name + '-build.log'
    $vstestPath = $name + '-vstest.log'
    $inprocPath = $name + '-inproc.log'

    $summary = 'NO SUMMARY (red build -> round void)'
    if (Test-Path $summaryPath) {
        $summary = ((Get-Content $summaryPath) -join ' ').Trim()
    }

    $counts = ''
    if (Test-Path $buildPath) {
        $errs = (Select-String -Path $buildPath -Pattern ': error ' -ErrorAction SilentlyContinue | Measure-Object).Count
        $warns = (Select-String -Path $buildPath -Pattern ': warning ' -ErrorAction SilentlyContinue | Measure-Object).Count
        $counts = 'errors=' + $errs + ' warnings=' + $warns
    }

    $vfails = -1
    if (Test-Path $vstestPath) {
        $vfails = (Select-String -Path $vstestPath -Pattern '\[FAIL\]' -ErrorAction SilentlyContinue | Measure-Object).Count
    }

    $ifails = -1
    if (Test-Path $inprocPath) {
        $ifails = (Select-String -Path $inprocPath -Pattern '\[FAIL\]' -ErrorAction SilentlyContinue | Measure-Object).Count
    }

    $vtail = ''
    if (Test-Path $vstestPath) { $vtail = (Get-Content $vstestPath -Tail 1) }
    $itail = ''
    if (Test-Path $inprocPath) { $itail = (Get-Content $inprocPath -Tail 1) }

    Write-Host ('--- round ' + $r + ' (' + $name + ') ---')
    Write-Host ('EXITS: ' + $summary)
    Write-Host ('BUILD: ' + $counts)
    Write-Host ('VSTEST  FAILs=' + $vfails + '  ' + $vtail)
    Write-Host ('INPROC  FAILs=' + $ifails + '  ' + $itail)
}
