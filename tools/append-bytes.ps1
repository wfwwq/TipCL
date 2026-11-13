# append-bytes.ps1 -- byte-level append (never Get-Content|Set-Content: D-36).
#
# WHY: this repo's docs (PROGRESS.md / DECISIONS.md) are UTF-8 without BOM and contain Chinese.
# PowerShell 5.1's Get-Content decodes such files as ANSI and silently eats newlines, so appending
# text through it corrupts the file. This script only touches bytes: read both files as bytes,
# optionally add one LF if the destination does not end with one, then WriteAllBytes.
#
# USAGE (ASCII only, per D-08):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\append-bytes.ps1 -Src <file> -Dst <file>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Src,
    [Parameter(Mandatory = $true)][string]$Dst
)

$ErrorActionPreference = 'Stop'

$srcPath = (Resolve-Path -LiteralPath $Src).Path
$dstPath = (Resolve-Path -LiteralPath $Dst).Path
$sec = [IO.File]::ReadAllBytes($srcPath)
$cur = [IO.File]::ReadAllBytes($dstPath)

$addLf = ($cur.Length -gt 0 -and $cur[$cur.Length - 1] -ne 10)
$all = New-Object byte[] ($cur.Length + $sec.Length + $(if ($addLf) { 1 } else { 0 }))
[Array]::Copy($cur, 0, $all, 0, $cur.Length)
if ($addLf) {
    $all[$cur.Length] = 10
    [Array]::Copy($sec, 0, $all, $cur.Length + 1, $sec.Length)
}
else {
    [Array]::Copy($sec, 0, $all, $cur.Length, $sec.Length)
}

[IO.File]::WriteAllBytes($dstPath, $all)
Write-Host ('appended ' + $sec.Length + ' bytes to ' + $dstPath + ' (now ' + $all.Length + ' bytes)')
