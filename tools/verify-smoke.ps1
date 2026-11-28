# TipCL headless smoke matrix runner (Windows/PowerShell 5.1 side).
#
# Runs the authoritative 8-frame headless smoke matrix against a built TipCL.App:
#
#   launch / library / download / plugins / settings / ai / library-dark / plugins-dark
#
# This is the set recorded in docs/dev/PROGRESS.md (M4-H R13 / main-agent recheck), see
# docs/PARITY.md (section "headless smoke matrix") for the exact per-frame env vars.
# It is NOT the same set as tools/verify-linux.sh used to cover - that script missed
# SMOKE_PAGE=ai and used SMOKE_DARK=1 on the default page (launch-dark) instead of
# library-dark / plugins-dark.
#
# Acceptance criteria (D-26) - all three must hold, per frame:
#   1. process exit code == 0
#   2. app log has 0 [WARN] and 0 [ERROR]
#   3. a human eyeballs the PNG (this script only prints it; it cannot eyeball)
# PNG byte size / sha256 are printed for reference ONLY. They are not an identity
# criterion (D-26, D-37: byte equality only applies inside a same-package isolated A/B,
# and even then plugins-dark / settings-tab6 frames carry run-to-run jitter).
#
# Isolation (D-43): every run gets its own TIPCL_HOME and TEMP/TMP under -OutDir.
# The real user directory (%LOCALAPPDATA%\TipCL) is never read or written.
# The variable name is deliberately NOT "$home" - that collides with the PowerShell
# read-only $HOME and silently redirects TIPCL_HOME to the user profile root (D-43).
#
# Usage (PowerShell 5.1; ExecutionPolicy is Restricted on this box):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\verify-smoke.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\verify-smoke.ps1 -Batches 2 -OutDir D:\tmp\smoke
#
# Build the app first (Release is what the release pipeline ships):
#   dotnet build TipCL.Linux.sln -c Release
#
# Concurrency: this script launches the app, so if another member may be building the
# same tree concurrently, pass -LockDir and it will serialize on that directory
# (the project build lock is <workroot>\progress\.build.lock).

param(
    [string]$Bin,
    [string]$OutDir,
    [int]$Batches = 2,
    [string]$LockDir,
    [switch]$SkipPerPage
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir

if (-not $OutDir) { $OutDir = Join-Path $RepoRoot 'dist\smoke-matrix' }
$OutDir = [IO.Path]::GetFullPath($OutDir)

# ---------------------------------------------------------------- app location
function Resolve-AppPath {
    param([string]$Explicit)
    if ($Explicit) {
        if (-not (Test-Path $Explicit)) { throw " -Bin not found: $Explicit" }
        return (Resolve-Path $Explicit).Path
    }
    foreach ($c in @(
        (Join-Path $RepoRoot 'src\TipCL.App\bin\Release\net8.0\TipCL.App.dll'),
        (Join-Path $RepoRoot 'src\TipCL.App\bin\Debug\net8.0\TipCL.App.dll'))) {
        if (Test-Path $c) { return (Resolve-Path $c).Path }
    }
    throw "TipCL.App.dll not found. Build first: dotnet build TipCL.Linux.sln -c Release"
}

# ------------------------------------------------------- optional build lock
function Enter-Lock {
    param([string]$Dir)
    if (-not $Dir) { return $false }
    $tries = 0
    while (-not (New-Item -ItemType Directory -Path $Dir -ErrorAction SilentlyContinue)) {
        $tries++
        if ($tries -gt 360) { throw "lock timeout: $Dir" }
        Start-Sleep -Seconds 5
    }
    return $true
}

function Exit-Lock {
    param([string]$Dir, [bool]$Held)
    if ($Held -and $Dir) { Remove-Item -Recurse -Force $Dir -ErrorAction SilentlyContinue }
}

# --------------------------------------------------------------- the matrix
# Name is the frame file name recorded in docs/dev/PROGRESS.md:402. Page '' = default page (launch).
$Matrix = @(
    [pscustomobject]@{ Name = 'launch';       Page = '';         Dark = $false },
    [pscustomobject]@{ Name = 'library';      Page = 'library';  Dark = $false },
    [pscustomobject]@{ Name = 'download';     Page = 'download'; Dark = $false },
    [pscustomobject]@{ Name = 'plugins';      Page = 'plugins';  Dark = $false },
    [pscustomobject]@{ Name = 'settings';     Page = 'settings'; Dark = $false },
    [pscustomobject]@{ Name = 'ai';           Page = 'ai';       Dark = $false },
    [pscustomobject]@{ Name = 'library-dark'; Page = 'library';  Dark = $true  },
    [pscustomobject]@{ Name = 'plugins-dark'; Page = 'plugins';  Dark = $true  }
)

function Get-FrameEnv {
    param([pscustomobject]$M)
    $parts = @()
    if ($M.Page) { $parts += ('SMOKE_PAGE=' + $M.Page) }
    if ($M.Dark) { $parts += 'SMOKE_DARK=1' }
    if ($parts.Count -eq 0) { return '(defaults)' }
    return ($parts -join ' ')
}

function Clear-SmokeEnv {
    foreach ($n in 'SMOKE_PAGE','SMOKE_DARK','SMOKE_TASKS','SMOKE_SEG','SMOKE_SETTINGS_TAB','SMOKE_WAIT','SMOKE_PROBE','SMOKE_NET','SMOKE_INSTALL') {
        Remove-Item "Env:$n" -ErrorAction SilentlyContinue
    }
}

function Invoke-OneFrame {
    param(
        [string]$AppPath,
        [string]$HomeDir,
        [string]$WorkDir,
        [string]$Page,
        [bool]$Dark
    )
    New-Item -ItemType Directory -Force -Path $HomeDir | Out-Null
    New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
    $png = Join-Path $WorkDir 'smoke.png'
    Remove-Item $png -ErrorAction SilentlyContinue

    $env:TIPCL_HOME = $HomeDir
    # D-43 guard: never let a miscomputed home escape to the real user profile.
    $resolvedHome = [IO.Path]::GetFullPath($HomeDir)
    if (-not $resolvedHome.StartsWith($OutDir, [StringComparison]::OrdinalIgnoreCase)) {
        throw "refusing to run: TIPCL_HOME '$resolvedHome' is outside -OutDir '$OutDir'"
    }
    $tempDir = Join-Path $WorkDir 'temp'
    New-Item -ItemType Directory -Force -Path $tempDir | Out-Null
    $env:TEMP = $tempDir
    $env:TMP  = $tempDir

    Clear-SmokeEnv
    if ($Page) { $env:SMOKE_PAGE = $Page }
    if ($Dark) { $env:SMOKE_DARK = '1' }

    $oLog = Join-Path $WorkDir 'run.out.log'
    $eLog = Join-Path $WorkDir 'run.err.log'
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process -FilePath 'dotnet' -ArgumentList @($AppPath, '--smoke') `
        -WorkingDirectory $WorkDir -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput $oLog -RedirectStandardError $eLog
    $sw.Stop()

    $bytes = 0; $sha = ''
    if (Test-Path $png) {
        $bytes = (Get-Item $png).Length
        $sha = (Get-FileHash -Algorithm SHA256 $png).Hash
    }

    $warn = 0; $err = 0
    $logsDir = Join-Path $HomeDir 'logs'
    if (Test-Path $logsDir) {
        foreach ($lf in Get-ChildItem $logsDir -Filter '*.log' -File) {
            $warn += (Select-String -Path $lf.FullName -Pattern '[WARN]' -SimpleMatch -ErrorAction SilentlyContinue | Measure-Object).Count
            $err  += (Select-String -Path $lf.FullName -Pattern '[ERROR]' -SimpleMatch -ErrorAction SilentlyContinue | Measure-Object).Count
        }
    }

    $stderr = ''
    if (Test-Path $eLog) { $stderr = (Get-Content $eLog -Raw) }
    if ($null -eq $stderr) { $stderr = '' }

    [pscustomobject]@{
        ExitCode = $proc.ExitCode
        Bytes    = $bytes
        Sha256   = $sha
        Warn     = $warn
        Error    = $err
        Ms       = $sw.ElapsedMilliseconds
        Png      = $png
        Stderr   = $stderr.Trim()
        Ok       = ($proc.ExitCode -eq 0 -and $bytes -gt 10240 -and $warn -eq 0 -and $err -eq 0)
    }
}

# ---------------------------------------------------------------------- run
$AppPath = Resolve-AppPath -Explicit $Bin
Write-Host "TipCL smoke matrix"
Write-Host ("  app     : {0}" -f $AppPath)
Write-Host ("  out     : {0}" -f $OutDir)
Write-Host ("  batchs  : {0}   per-page passes: {1}" -f $Batches, (-not $SkipPerPage))
Write-Host ("  frames  : {0}" -f (($Matrix | ForEach-Object { $_.Name }) -join ' / '))

$lockHeld = Enter-Lock -Dir $LockDir
try {
    $failures = New-Object System.Collections.ArrayList
    $rows = New-Object System.Collections.ArrayList

    # --- pass A: one fresh TIPCL_HOME per frame (cold start each time; clean per-frame log)
    if (-not $SkipPerPage) {
        Write-Host ''
        Write-Host '== pass A: cold start per frame (isolated TIPCL_HOME each) =='
        foreach ($m in $Matrix) {
            $isolatedHome = Join-Path $OutDir ("home-A-$($m.Name)")
            $work = Join-Path $OutDir ("work-A-$($m.Name)")
            $r = Invoke-OneFrame -AppPath $AppPath -HomeDir $isolatedHome -WorkDir $work -Page $m.Page -Dark $m.Dark
            $rows.Add([pscustomobject]@{ Pass='A'; Frame=$m.Name; Env=(Get-FrameEnv $m); Exit=$r.ExitCode; Bytes=$r.Bytes; Warn=$r.Warn; Error=$r.Error; Ms=$r.Ms; Ok=$r.Ok }) | Out-Null
            $verdict = if ($r.Ok) { 'OK' } else { 'FAIL' }
            Write-Host ("  {0,-14} exit={1} bytes={2,-8} warn={3} error={4} {5}ms  {6}" -f $m.Name, $r.ExitCode, $r.Bytes, $r.Warn, $r.Error, $r.Ms, $verdict)
            if (-not $r.Ok) {
                $failures.Add(("pass A frame '{0}': exit={1} bytes={2} warn={3} error={4} stderr={5}" -f $m.Name, $r.ExitCode, $r.Bytes, $r.Warn, $r.Error, $r.Stderr)) | Out-Null
            }
        }
    }

    # --- pass B: N batches, ONE fresh TIPCL_HOME per batch, 8 frames sequentially.
    # This is the historical protocol (cold start per batch) that R8's "previous page frame
    # written under the new file name" was observed in.
    for ($b = 1; $b -le $Batches; $b++) {
        Write-Host ''
        Write-Host ("== pass B batch {0}/{1}: cold start once, 8 frames sequentially ==" -f $b, $Batches)
        $isolatedHome = Join-Path $OutDir ("home-B{0}" -f $b)
        Remove-Item $isolatedHome -Recurse -Force -ErrorAction SilentlyContinue
        $shas = @{}
        foreach ($m in $Matrix) {
            $work = Join-Path $OutDir ("work-B{0}-{1}" -f $b, $m.Name)
            $r = Invoke-OneFrame -AppPath $AppPath -HomeDir $isolatedHome -WorkDir $work -Page $m.Page -Dark $m.Dark
            $rows.Add([pscustomobject]@{ Pass=("B$b"); Frame=$m.Name; Env=(Get-FrameEnv $m); Exit=$r.ExitCode; Bytes=$r.Bytes; Warn=$r.Warn; Error=$r.Error; Ms=$r.Ms; Ok=$r.Ok }) | Out-Null
            $verdict = if ($r.Ok) { 'OK' } else { 'FAIL' }
            Write-Host ("  {0,-14} exit={1} bytes={2,-8} warn={3} error={4} {5}ms  {6}" -f $m.Name, $r.ExitCode, $r.Bytes, $r.Warn, $r.Error, $r.Ms, $verdict)
            if (-not $r.Ok) {
                $failures.Add(("pass B${b} frame '{0}': exit={1} bytes={2} warn={3} error={4} stderr={5}" -f $m.Name, $r.ExitCode, $r.Bytes, $r.Warn, $r.Error, $r.Stderr)) | Out-Null
            }
            if ($r.Sha256) {
                if ($shas.ContainsKey($r.Sha256)) {
                    $failures.Add(("pass B${b}: frames '{0}' and '{1}' rendered byte-identical ({2}) - smells like the R8 stale-frame defect" -f $shas[$r.Sha256], $m.Name, $r.Sha256)) | Out-Null
                } else {
                    $shas[$r.Sha256] = $m.Name
                }
            }
        }
    }

    # --- summary
    Write-Host ''
    Write-Host '== summary =='
    $rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
    $csv = Join-Path $OutDir 'matrix.csv'
    $rows | Export-Csv -NoTypeInformation -Encoding UTF8 $csv
    Write-Host ("wrote {0}" -f $csv)

    if ($failures.Count -gt 0) {
        Write-Host ''
        Write-Host ("FAILURES ({0}):" -f $failures.Count) -ForegroundColor Red
        foreach ($f in $failures) { Write-Host ("  - {0}" -f $f) -ForegroundColor Red }
        Write-Host ''
        Write-Host 'NOT verified: exit code / 0-WARN-0-ERROR criteria did not hold for every frame.' -ForegroundColor Red
        exit 1
    }

    Write-Host ''
    Write-Host 'exit code 0 and 0 WARN / 0 ERROR for every frame.' -ForegroundColor Green
    Write-Host ("STILL MANUAL (D-26 criterion 1): open the PNGs under {0}\work-* and eyeball them." -f $OutDir) -ForegroundColor Yellow
    exit 0
}
finally {
    Clear-SmokeEnv
    Exit-Lock -Dir $LockDir -Held $lockHeld
}
