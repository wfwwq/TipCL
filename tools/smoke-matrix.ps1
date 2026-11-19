# tools/smoke-matrix.ps1 —— 逐页无头冒烟矩阵(Windows / PowerShell 5.1 侧)
#
# 目的:把「每一页都能无头打开、都不炸」变成一条**可重跑的命令**,而不是人手翻截屏。
# 用户反馈里最贵的一类缺陷是「某个页面点进去就白屏/报错」——它在 CI 的默认帧(只拍启动页)里
# 完全看不见。这里对**每个页面 id** 各起一次独立进程、各给一份隔离 TIPCL_HOME,
# 用四条判据卡住:
#   1) 进程退出码 == 0
#   2) smoke.png > -MinPngBytes(默认 10 KB;近空白/纯背景的帧会被这条挡住)
#   3) 日志 0 个 [WARN]
#   4) 日志 0 个 [ERROR]
# 任意一页不达标 ⇒ 脚本 exit 1,并打印该页的 console 尾部 + 日志里的 WARN/ERROR 原文。
#
# 用法(在仓库根):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke-matrix.ps1
#   powershell ... -File tools\smoke-matrix.ps1 -Bin src\TipCL.App\bin\Release\net8.0\TipCL.App.dll
#   powershell ... -File tools\smoke-matrix.ps1 -Pages launch,settings -OutDir D:\tmp\matrix
#   powershell ... -File tools\smoke-matrix.ps1 -Markdown progress\smoke-matrix.md
#
# 与 tools/verify-smoke.ps1 的分工:那个管「同一页 x 明暗 x 多批」的视觉回归矩阵(8 帧),
# 本脚本管「**所有页面** 都开得起来」的门槛检查。两者判据同源(D-26):退出码 + png > 10 KB + 0 WARN / 0 ERROR。
#
# Linux/CI 侧的等价实现:tools/smoke-matrix.sh(同一套页面清单与判据,两边要一起改)。

param(
    [string]$Bin,
    [string]$OutDir,
    [string[]]$Pages,
    [int]$MinPngBytes = 10240,
    [int]$TimeoutSec = 180,
    [string]$Markdown,
    [switch]$KeepHomes
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir

if (-not $Pages -or $Pages.Count -eq 0) {
    # 页面 id 的来源:src/TipCL.App/Shell/MainWindow.axaml.cs 的 GetPage(id) switch
    # (launch / library / instance=版本管理 / download / plugins / settings / multiplayer / ai / account / tasks)
    $Pages = @('launch', 'download', 'library', 'plugins', 'settings', 'instance', 'account', 'multiplayer', 'tasks', 'ai')
}
if (-not $OutDir) { $OutDir = Join-Path $RepoRoot 'dist\smoke-matrix' }
$OutDir = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# ---------------------------------------------------------------- app location
function Resolve-AppPath {
    param([string]$Explicit)
    if ($Explicit) {
        $full = [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Explicit))
        if (-not (Test-Path $full)) { $full = [IO.Path]::GetFullPath($Explicit) }
        if (-not (Test-Path $full)) { throw "-Bin 不存在: $Explicit" }
        return (Resolve-Path $full).Path
    }
    foreach ($c in @(
        (Join-Path $RepoRoot 'src\TipCL.App\bin\Release\net8.0\TipCL.App.dll'),
        (Join-Path $RepoRoot 'src\TipCL.App\bin\Debug\net8.0\TipCL.App.dll'))) {
        if (Test-Path $c) { return (Resolve-Path $c).Path }
    }
    throw "找不到 TipCL.App.dll —— 先构建: dotnet build src/TipCL.App/TipCL.App.csproj -c Release"
}

# dll 用 dotnet 宿主跑;exe(AppImage/单文件)直接跑
function New-Runner {
    param([string]$AppPath)
    if ($AppPath.ToLowerInvariant().EndsWith('.dll')) {
        return @{ Exe = 'dotnet'; Args = @($AppPath, '--smoke') }
    }
    return @{ Exe = $AppPath; Args = @('--smoke') }
}

function Clear-SmokeEnv {
    foreach ($n in 'SMOKE_PAGE', 'SMOKE_DARK', 'SMOKE_TASKS', 'SMOKE_SEG', 'SMOKE_SETTINGS_TAB',
        'SMOKE_WAIT', 'SMOKE_PROBE', 'SMOKE_NET', 'SMOKE_INSTALL', 'SMOKE_INSTANCE', 'SMOKE_MARKER') {
        Remove-Item "Env:$n" -ErrorAction SilentlyContinue
    }
}

function Write-Utf8NoBom {
    param([string]$Path, [string]$Text)
    [IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

function Count-Marker {
    param([string]$Path, [string]$Pattern)
    if (-not (Test-Path $Path)) { return 0 }
    $n = (Select-String -Path $Path -Pattern $Pattern -SimpleMatch -ErrorAction SilentlyContinue | Measure-Object).Count
    if ($null -eq $n) { return 0 }
    return $n
}

$AppPath = Resolve-AppPath -Explicit $Bin
$runner = New-Runner -AppPath $AppPath

Write-Host 'TipCL 冒烟矩阵(逐页:能否无头打开、是否 0 WARN / 0 ERROR)'
Write-Host ("  app      : {0}" -f $AppPath)
Write-Host ("  out      : {0}" -f $OutDir)
Write-Host ("  pages    : {0}" -f ($Pages -join ' / '))
Write-Host ("  criteria : exit=0, smoke.png > {0} B, 0 [WARN], 0 [ERROR]" -f $MinPngBytes)
Write-Host ''

$rows = New-Object System.Collections.ArrayList
$failures = New-Object System.Collections.ArrayList

foreach ($page in $Pages) {
    $homeDir = Join-Path $OutDir ("home-" + $page)
    $work = Join-Path $OutDir ("work-" + $page)
    if (-not $KeepHomes) { Remove-Item $homeDir, $work -Recurse -Force -ErrorAction SilentlyContinue }
    New-Item -ItemType Directory -Force -Path $homeDir, $work | Out-Null

    # 首启提示/反馈同意预先写死,免得每页都弹一次首启对话框(那会让每页帧都不一样)
    Write-Utf8NoBom -Path (Join-Path $homeDir 'config.json') -Text '{"first_run": false, "feedback_consent": true}'

    $resolvedHome = [IO.Path]::GetFullPath($homeDir)
    if (-not $resolvedHome.StartsWith($OutDir, [StringComparison]::OrdinalIgnoreCase)) {
        throw ("拒绝运行: TIPCL_HOME '{0}' 不在 -OutDir '{1}' 之内(别把测试写进真实用户目录)" -f $resolvedHome, $OutDir)
    }

    $env:TIPCL_HOME = $homeDir
    $env:TEMP = $work
    $env:TMP = $work
    Clear-SmokeEnv
    $env:SMOKE_PAGE = $page

    $console = Join-Path $work 'console.log'
    # 用 .NET Process 而不是 Start-Process -PassThru:后者在 PS 5.1 里拿到的 Process 常常
    # 读不出 ExitCode($null),判据会全变红(实测:exit= 空 → 10 页全 FAIL)。
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $runner.Exe
    $psi.Arguments = (($runner.Args | ForEach-Object { '"' + $_ + '"' }) -join ' ')
    $psi.WorkingDirectory = $work
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $sw = [Diagnostics.Stopwatch]::StartNew()
    $proc = [System.Diagnostics.Process]::Start($psi)
    $outTask = $proc.StandardOutput.ReadToEndAsync()
    $errTask = $proc.StandardError.ReadToEndAsync()
    $timedOut = $false
    if (-not $proc.WaitForExit($TimeoutSec * 1000)) {
        $timedOut = $true
        try { $proc.Kill() } catch { }
    }
    try { $proc.WaitForExit() } catch { }
    $sw.Stop()
    $code = if ($timedOut) { -1 } else { $proc.ExitCode }
    $stdout = ''
    $stderr = ''
    try { $stdout = $outTask.Result } catch { }
    try { $stderr = $errTask.Result } catch { }
    Write-Utf8NoBom -Path $console -Text ($stdout + $stderr)

    $png = Join-Path $work 'smoke.png'
    $pngBytes = if (Test-Path $png) { (Get-Item $png).Length } else { 0 }

    # WARN/ERROR 既可能在 console(镜像),也可能只进文件日志(TIPCL_HOME/logs/*.log)—— 两处都数
    $warn = Count-Marker -Path $console -Pattern '[WARN]'
    $err = Count-Marker -Path $console -Pattern '[ERROR]'
    $logsDir = Join-Path $homeDir 'logs'
    if (Test-Path $logsDir) {
        foreach ($lf in Get-ChildItem $logsDir -Filter '*.log' -File -ErrorAction SilentlyContinue) {
            $warn += Count-Marker -Path $lf.FullName -Pattern '[WARN]'
            $err += Count-Marker -Path $lf.FullName -Pattern '[ERROR]'
        }
    }

    $ok = (-not $timedOut) -and ($code -eq 0) -and ($pngBytes -gt $MinPngBytes) -and ($warn -eq 0) -and ($err -eq 0)
    $rows.Add([pscustomobject]@{
            Page = $page; Ok = $ok; Exit = $code; PngBytes = $pngBytes
            Warn = $warn; Error = $err; Ms = $sw.ElapsedMilliseconds; TimedOut = $timedOut
        }) | Out-Null

    Write-Host ("  {0,-12} exit={1,-3} png={2,-8} warn={3} error={4} {5}ms  {6}" -f `
            $page, $code, $pngBytes, $warn, $err, $sw.ElapsedMilliseconds, $(if ($ok) { 'OK' } else { 'FAIL' }))

    if (-not $ok) {
        $failures.Add(("page '{0}': exit={1} timedOut={2} png={3}B warn={4} error={5}" -f $page, $code, $timedOut, $pngBytes, $warn, $err)) | Out-Null
        # 把该页的现场直接打出来:不登机器也能知道为什么红
        if (Test-Path $console) {
            Write-Host '    --- console(尾部 20 行)---'
            Get-Content $console -Tail 20 | ForEach-Object { Write-Host ("    " + $_) }
        }
        foreach ($src in @($console) + @(Get-ChildItem $logsDir -Filter '*.log' -File -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })) {
            if (-not (Test-Path $src)) { continue }
            $hits = Select-String -Path $src -Pattern '\[(WARN|ERROR)\]' -ErrorAction SilentlyContinue | Select-Object -First 10
            foreach ($hit in $hits) { Write-Host ("    " + $hit.Line.Trim()) }
        }
    }
}

Clear-SmokeEnv

# ------------------------------------------------------------------- summary
Write-Host ''
Write-Host '== 汇总 =='
$rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

$csv = Join-Path $OutDir 'matrix.csv'
$rows | Export-Csv -NoTypeInformation -Encoding UTF8 $csv
Write-Host ("wrote {0}" -f $csv)

if ($Markdown) {
    $md = New-Object System.Text.StringBuilder
    [void]$md.AppendLine('| 页面 | 结论 | exit | smoke.png (B) | [WARN] | [ERROR] | 耗时 (ms) |')
    [void]$md.AppendLine('| --- | --- | --- | --- | --- | --- | --- |')
    foreach ($r in $rows) {
        [void]$md.AppendLine(("| {0} | {1} | {2} | {3} | {4} | {5} | {6} |" -f `
                    $r.Page, $(if ($r.Ok) { 'OK' } else { 'FAIL' }), $r.Exit, $r.PngBytes, $r.Warn, $r.Error, $r.Ms))
    }
    $mdPath = if ([IO.Path]::IsPathRooted($Markdown)) { $Markdown } else { Join-Path (Get-Location).Path $Markdown }
    $mdPath = [IO.Path]::GetFullPath($mdPath)
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $mdPath) | Out-Null
    Write-Utf8NoBom -Path $mdPath -Text $md.ToString()
    Write-Host ("wrote {0}" -f $mdPath)
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host ("FAILURES ({0}):" -f $failures.Count) -ForegroundColor Red
    foreach ($f in $failures) { Write-Host ("  - {0}" -f $f) -ForegroundColor Red }
    Write-Host ''
    Write-Host '至少一页没达标:退出码 0 / png > 10 KB / 0 WARN / 0 ERROR 没有全部成立。' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host ("全部 {0} 页通过:exit=0、smoke.png > {1} B、0 WARN、0 ERROR。" -f $rows.Count, $MinPngBytes) -ForegroundColor Green
exit 0