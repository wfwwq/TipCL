# c5a-append-docs.ps1 -- append the C5a doc sections to PROGRESS.md / DECISIONS.md BY BYTES.
#
# ASCII only (D-08). WHY bytes: D-36 forbids `Get-Content | Set-Content` on files that contain Chinese --
# PowerShell 5.1's default text pipeline decodes UTF-8-without-BOM as ANSI and both mangles the text and
# collapses the LF-only line endings. So: read both files as bytes, concatenate, write back.
# A LF is inserted first if the destination does not already end with one.
#
# USAGE (through the docs lock):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\with-build-lock.ps1 `
#       -Name TipCLMacDocs -What "powershell -NoProfile -ExecutionPolicy Bypass -File tools\c5a-append-docs.ps1" -TimeoutSec 300

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Get-BytesExact {
    param([string]$Path)
    return [System.IO.File]::ReadAllBytes((Join-Path $root $Path))
}

$pairs = @(
    @{ Src = 'artifacts\c5a-progress.md';  Dst = 'PROGRESS.md' },
    @{ Src = 'artifacts\c5a-decisions.md'; Dst = 'DECISIONS.md' }
)

foreach ($p in $pairs) {
    $srcFull = Join-Path $root $p.Src
    if (-not (Test-Path $srcFull)) {
        Write-Host ("skip {0} (no section file)" -f $p.Dst)
        continue
    }

    $dst = Get-BytesExact -Path $p.Dst
    $add = Get-BytesExact -Path $p.Src
    $sep = @()
    if ($dst.Length -gt 0 -and $dst[$dst.Length - 1] -ne 10) {
        $sep = @(10)
    }

    $out = New-Object byte[] ($dst.Length + $sep.Length + $add.Length)
    [Array]::Copy($dst, 0, $out, 0, $dst.Length)
    if ($sep.Length -gt 0) { $out[$dst.Length] = 10 }
    [Array]::Copy($add, 0, $out, $dst.Length + $sep.Length, $add.Length)
    [System.IO.File]::WriteAllBytes((Join-Path $root $p.Dst), $out)

    Write-Host ("appended {0} bytes to {1} (was {2}, now {3})" -f $add.Length, $p.Dst, $dst.Length, $out.Length)
}
