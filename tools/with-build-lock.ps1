# with-build-lock.ps1 -- serialize dotnet build/test/smoke across agents in this repo.
#
# WHY: two agents building or testing this solution at the same time clobber each other's
# obj/bin output. That already happened once in this project (a run went from green to
# 115 failures while another session rebuilt the same output tree). Any agent that runs
# dotnet must wrap the command in this script.
#
# USAGE (PowerShell 5.1 safe, ASCII only per D-08):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\with-build-lock.ps1 `
#       -What 'dotnet test TipCL.Mac.sln -c Release --no-build' -TimeoutSec 1800
#
# The inner command is run through cmd.exe so that quoting matches what the task briefs
# use elsewhere, and its exit code becomes this script's exit code.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$What,
    [int]$TimeoutSec = 1800,
    [string]$Name = 'TipCLMacDotnet'
)

$ErrorActionPreference = 'Stop'

$mutex = New-Object System.Threading.Mutex($false, ('Local\' + $Name))
$held = $false
try {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $held = $mutex.WaitOne([int]($TimeoutSec * 1000), $false)
    }
    catch [System.Threading.AbandonedMutexException] {
        # the previous holder died without releasing; we own it now
        $held = $true
    }

    if (-not $held) {
        Write-Host ('[lock] FAILED to acquire ' + $Name + ' within ' + $TimeoutSec + 's')
        exit 3
    }

    Write-Host ('[lock] acquired ' + $Name + ' after ' + [int]$sw.Elapsed.TotalSeconds + 's')
    & cmd.exe /c $What
    $code = $LASTEXITCODE
    Write-Host ('[lock] command exit code ' + $code + '; releasing')
    exit $code
}
finally {
    if ($held) {
        try { $mutex.ReleaseMutex() } catch { }
    }
    $mutex.Dispose()
}
