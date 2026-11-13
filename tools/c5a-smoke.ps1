# c5a-smoke.ps1 -- C5a smoke frames + baseline comparison + a little pixel evidence.
#
# ASCII only (D-08). Runs the already-built headless smoke runner 5 times:
#   no --page            -> must stay byte-identical to the C3c baseline  (37681 / 2603BF6A...)
#   --page launch dark   -> must stay byte-identical to the C3c baseline  (33579 / 7D7AFDD5...)
#   --page settings light/dark -> the NEW baselines of this package
#   --theme blue         -> must still exit 2 (bad argument) and write nothing
#
# WHY cmd /v:on: TipCL.exe is a GUI-subsystem (WinExe) binary, so PowerShell's `&` does NOT wait for it
# and $LASTEXITCODE stays empty (measured). `Start-Process -Wait` / `WaitForExit` hang in this sandbox
# (cross-process handle waits are denied). Routing through `cmd /v:on /c` with stdout+stderr redirected
# to a file makes cmd wait for the child, and `!errorlevel!` (delayed expansion) then reads the real code.
#
# USAGE (through the build lock so nothing rebuilds the shared bin/ mid-run):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\with-build-lock.ps1 `
#       -Name TipCLMacSmoke -What "powershell -NoProfile -ExecutionPolicy Bypass -File tools\c5a-smoke.ps1" -TimeoutSec 1200

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
Write-Host ("repo root: {0}" -f $root)

$exe = (Resolve-Path 'src\TipCL.App\bin\Release\net8.0\TipCL.exe').Path

function Invoke-Smoke {
    param([string]$Name, [string[]]$SmokeArgs, [string]$Out)

    if (Test-Path $Out) { Remove-Item $Out -Force }
    $logFile = 'artifacts\c5a-smoke-' + $Name + '.log'
    $exitFile = 'artifacts\c5a-smoke-' + $Name + '.exit'
    foreach ($f in @($logFile, $exitFile)) { if (Test-Path $f) { Remove-Item $f -Force } }

    $inner = '"' + $exe + '" --smoke ' + ($SmokeArgs -join ' ') + ' --out "' + $Out + '" > "' + $logFile +
             '" 2>&1 & echo EXIT=!errorlevel! > "' + $exitFile + '"'
    cmd /v:on /c $inner | Out-Null

    $code = -1
    if (Test-Path $exitFile) { $code = [int]((Get-Content $exitFile -Raw).Trim() -replace 'EXIT=', '') }
    $log = ''
    if (Test-Path $logFile) { $log = ((Get-Content $logFile -Raw) -replace "`r?`n", ' ').Trim() }

    if (Test-Path $Out) {
        $item = Get-Item $Out
        $hash = (Get-FileHash $Out -Algorithm SHA256).Hash
        Write-Host ("{0}: exit={1} size={2} sha256={3}" -f $Name, $code, $item.Length, $hash)
        Write-Host ("    log={0}" -f $log)
        return [pscustomobject]@{ Name = $Name; Exit = $code; Size = $item.Length; Sha = $hash }
    }

    Write-Host ("{0}: exit={1} (no file written)" -f $Name, $code)
    Write-Host ("    log={0}" -f $log)
    return [pscustomobject]@{ Name = $Name; Exit = $code; Size = 0; Sha = '' }
}

$baselineNoPage = '2603BF6AB2B5D491E2B6AFB34E3EAEDD8A0388B6A6497D33D3F8193492342CBD'
$baselineDark = '7D7AFDD502D82EE21F7DCEFEF7B12FC2731628D41B4DDF595FD1E1D05F276893'

$noPage = Invoke-Smoke -Name 'no-page' -SmokeArgs @() -Out 'artifacts\c5a-nopage.png'
$launch = Invoke-Smoke -Name 'launch-dark' -SmokeArgs @('--page', 'launch', '--theme', 'dark') -Out 'artifacts\c5a-launch-dark.png'
$setLight = Invoke-Smoke -Name 'settings-light' -SmokeArgs @('--page', 'settings', '--theme', 'light') -Out 'artifacts\c5a-settings-light.png'
$setDark = Invoke-Smoke -Name 'settings-dark' -SmokeArgs @('--page', 'settings', '--theme', 'dark') -Out 'artifacts\c5a-settings-dark.png'
$blue = Invoke-Smoke -Name 'theme-blue' -SmokeArgs @('--theme', 'blue') -Out 'artifacts\c5a-blue.png'

Write-Host ''
Write-Host ("BASELINE no-page    : {0} / {1}  (expected 37681 / {2})" -f $noPage.Size, $noPage.Sha, $baselineNoPage)
Write-Host ("BASELINE launch-dark: {0} / {1}  (expected 33579 / {2})" -f $launch.Size, $launch.Sha, $baselineDark)
Write-Host ("BASELINE blue       : exit {0}  (expected 2)" -f $blue.Exit)
Write-Host ''
Write-Host 'NEW baselines:'
Write-Host ("  --page settings --theme light : {0} / {1}" -f $setLight.Size, $setLight.Sha)
Write-Host ("  --page settings --theme dark  : {0} / {1}" -f $setDark.Size, $setDark.Sha)
if ($noPage.Sha -eq $baselineNoPage) { Write-Host 'no-page: byte-identical to the C3c baseline' } else { Write-Host 'no-page: CHANGED -- STOP and report' }
if ($launch.Sha -eq $baselineDark) { Write-Host 'launch-dark: byte-identical to the C3c baseline' } else { Write-Host 'launch-dark: CHANGED -- STOP and report' }

# ---------------------------------------------------------------- pixel evidence
Add-Type -AssemblyName System.Drawing

function Measure-Frame {
    param([string]$Label, [string]$File)
    $bmp = [System.Drawing.Bitmap]::FromFile((Resolve-Path $File).Path)
    try {
        $w = $bmp.Width
        $h = $bmp.Height
        $tally = @{}
        for ($y = 0; $y -lt $h; $y += 2) {
            for ($x = 0; $x -lt $w; $x += 2) {
                $c = $bmp.GetPixel($x, $y)
                $key = ('{0:X2}{1:X2}{2:X2}' -f $c.R, $c.G, $c.B)
                if ($tally.ContainsKey($key)) { $tally[$key] = $tally[$key] + 1 } else { $tally[$key] = 1 }
            }
        }
        $dominant = ($tally.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1)
        $total = 0
        foreach ($v in $tally.Values) { $total = $total + $v }

        $navInk = 0
        $contentInk = 0
        $swatch = @{}
        foreach ($key in @('2E9B6B', '2F72C4', '7C5CFC', 'D68A17', 'D64545', '149B9B')) { $swatch[$key] = 0 }
        for ($y = 0; $y -lt $h; $y += 2) {
            for ($x = 0; $x -lt $w; $x += 2) {
                $c = $bmp.GetPixel($x, $y)
                $key = ('{0:X2}{1:X2}{2:X2}' -f $c.R, $c.G, $c.B)
                if ($swatch.ContainsKey($key)) { $swatch[$key] = $swatch[$key] + 1 }
                if ($key -ne $dominant.Key) {
                    if ($x -lt 240) { $navInk = $navInk + 1 } else { $contentInk = $contentInk + 1 }
                }
            }
        }

        $swatchText = @()
        foreach ($key in @('2E9B6B', '2F72C4', '7C5CFC', 'D68A17', 'D64545', '149B9B')) {
            $swatchText = $swatchText + ('#' + $key + '=' + $swatch[$key])
        }

        Write-Host ("{0}: {1}x{2} sampled={3} dominant=#{4} ({5}%) left-band-ink={6} right-of-nav-ink={7}" -f `
            $Label, $w, $h, $total, $dominant.Key, [int](100.0 * $dominant.Value / $total), $navInk, $contentInk)
        Write-Host ("    distinct sampled colours={0}" -f $tally.Count)
        Write-Host ("    theme-swatch pixel counts: {0}" -f ($swatchText -join ' '))
    }
    finally {
        $bmp.Dispose()
    }
}

Measure-Frame -Label 'settings-light' -File 'artifacts\c5a-settings-light.png'
Measure-Frame -Label 'settings-dark' -File 'artifacts\c5a-settings-dark.png'

# ---------------------------------------------------------------- C6a:the whole page matrix
# 每个页面明暗两张都跑一遍(既有帧的期望字节数请对照 README.md 的矩阵表;download 是本包新注册的页)。
$pages = 'library', 'instance', 'tasks', 'account', 'download'
foreach ($p in $pages) {
    $l = Invoke-Smoke -Name ($p + '-light') -SmokeArgs @('--page', $p, '--theme', 'light') -Out ('artifacts\c6a-' + $p + '-light.png')
    $d = Invoke-Smoke -Name ($p + '-dark') -SmokeArgs @('--page', $p, '--theme', 'dark') -Out ('artifacts\c6a-' + $p + '-dark.png')
    Write-Host ("PAGE {0}: light={1} {2} dark={3} {4}" -f $p, $l.Size, $l.Sha, $d.Size, $d.Sha)
}

$l = [System.Drawing.Bitmap]::FromFile((Resolve-Path 'artifacts\c5a-settings-light.png').Path)
$d = [System.Drawing.Bitmap]::FromFile((Resolve-Path 'artifacts\c5a-settings-dark.png').Path)
try {
    $diff = 0
    $seen = 0
    for ($y = 0; $y -lt $l.Height; $y += 4) {
        for ($x = 0; $x -lt $l.Width; $x += 4) {
            $seen = $seen + 1
            if ($l.GetPixel($x, $y).ToArgb() -ne $d.GetPixel($x, $y).ToArgb()) { $diff = $diff + 1 }
        }
    }
    Write-Host ("settings light vs dark: {0}/{1} sampled pixels differ ({2}%)" -f $diff, $seen, [int](100.0 * $diff / $seen))
}
finally {
    $l.Dispose()
    $d.Dispose()
}
