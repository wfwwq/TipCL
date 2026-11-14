# flake-hunt.ps1 -- run N rounds of "atomic four-run" (build + VSTest + in-proc) inside ONE lock hold each,
# and print one line per round so a "three consecutive green rounds" acceptance is a single command.
#
# WHY: the acceptance gate is "both channels green three rounds in a row on a frozen tree". Reading the
# per-round log files by hand every time is error-prone (this project already lost rounds to reading the
# wrong file, see operational rule 10).
#
# ASCII only in this file on purpose (D-08/D-36): PowerShell 5.1 reads a BOM-less .ps1 as ANSI, so Chinese
# literals would be mangled -- hence we match only ASCII markers ("[FAIL]", "EXIT") and print the log
# tails verbatim (whatever language they are).
#
# USAGE (this script acquires the build lock itself for every round, so never wrap it in another lock):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\flake-hunt.ps1 -Rounds 3 -Prefix d1
#
# Each round r writes artifacts\<Prefix>-r<r>-{build.log,vstest.log,inproc.log,summary.txt}.

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
    $name = "$Prefix-r$r"
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\with-build-lock.ps1 `
        -What "artifacts\lead-verify.cmd artifacts\$name" -TimeoutSec $TimeoutSec | Out-Null

    $summaryPath = "artifacts\$name-summary.txt"
    $buildPath = "artifacts\$name-build.log"
    $vstestPath = "artifacts\$name-vstest.log"
    $inprocPath = "artifacts\$name-inproc.log"

    $exits = if (Test-Path $summaryPath) {
        ((Get-Content $summaryPath -Raw) -replace "`r?`n", ' ').Trim()
    } else {
        'NO SUMMARY (probably a red build -- round void)'
    }

    $warn = if (Test-Path $buildPath) {
        ((Select-String -Path $buildPath -Pattern '个警告|个错误' -ErrorAction SilentlyContinue |
            ForEach-Object { $_.Line.Trim() }) -join ' ')
    } else { '' }

    $vfail = if (Test-Path $vstestPath) {
        (Select-String -Path $vstestPath -Pattern '\[FAIL\]' -ErrorAction SilentlyContinue | Measure-Object).Count
    } else { -1 }

    $ifail = if (Test-Path $inprocPath) {
        (Select-String -Path $inprocPath -Pattern '\[FAIL\]' -ErrorAction SilentlyContinue | Measure-Object).Count
    } else { -1 }

    $vtail = if (Test-Path $vstestPath) { (Get-Content $vstestPath -Tail 1) } else { '' }
    $itail = if (Test-Path $inprocPath) { (Get-Content $inprocPath -Tail 1) } else { '' }

    Write-Host ("{0} | {1}| build: {2} | vstest FAILs={3} | inproc FAILs={4}" -f $name, $exits, $warn, $vfail, $ifail)
    Write-Host ("    vstest tail: {0}" -f $vtail)
    Write-Host ("    inproc tail: {0}" -f $itail)
}
