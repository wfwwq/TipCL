# windos/tools/smoke-matrix.ps1 —— WPF 侧「真跑一遍再判据」的冒烟矩阵(WPF / net48)
#
# 背景:net48 版 `--smoke` 不是"拍一张 smoke.png 就退出",而是**真开窗口、遍历每一页、
# 点一批按钮、开一批对话框**,把结果写成 <TIPCL_HOME>/_wpf_smoke/run-<时间戳>/report.json
# 外加每页/每个对话框的截图。这份 report 里已经有全部证据,但过去只能人工跑一次、人工看 —
# 结论没落盘、没判据、也没人能在下一个人身上重跑。
#
# 本脚本做三件事:
#   1) 在**隔离的 TIPCL_HOME** 下跑一次 TipCL.exe --smoke(不碰真实用户目录);
#   2) 按下面 9 条判据判红/绿(判据与 windos/tests/TipCL.Tests/SmokeReportCriteriaTests.cs 一致);
#   3) 打印每页/每次点击的表格,并把 Markdown 写进 -Markdown(可直接贴进报告)。
#
# 判据(全过才 exit 0):
#   C1 进程退出码 == 0
#   C2 report.json 存在且能解析,state == "finished"
#   C3 issues 为空(未处理异常 / 点击后挂住的异步操作都记在这里)
#   C4 至少 -MinPages 页(默认 10),且**每一页** ok == true
#   C5 点击数 >= report.required_clicks,且每次点击 ok == true
#   C6 对话框数 >= report.required_dialogs
#   C7 每页截图文件存在、> -MinShotBytes(默认 10 KB)、且不是 "截图失败:..." 这种字符串
#   C8 i18n_missing 为空(zh_CN 下不该有 L() 回落)
#   C9 没有 watchdog 超时(state != "watchdog-timeout",退出码 != 99)
#
# 用法(仓库根):
#   powershell -NoProfile -ExecutionPolicy Bypass -File windos\tools\smoke-matrix.ps1
#   ... -Exe windos\src\TipCL\bin\Release\net48\TipCL.exe -Markdown progress\wpf-smoke-matrix.md
#   ... -Clean       # 成功时删掉隔离目录(默认保留:report.json + 截图是唯一证据)
#
# 注意:WPF 版需要**有桌面会话**(托管 CI runner 没有交互桌面,那条只做 best-effort);
# 本脚本是"本机/真机回归"用的,CI 上的等价覆盖是 ci.yml 的 verify-linux(ABI 不同,判据同源)。

param(
    [string]$Exe,
    [string]$OutDir,
    [string]$Markdown,
    [int]$MinPages = 10,
    [int]$MinShotBytes = 10240,
    [int]$TimeoutSec = 600,
    [string]$Lang = 'zh_CN',
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$WinRoot = Split-Path -Parent $ScriptDir
$RepoRoot = Split-Path -Parent $WinRoot

if (-not $Exe) {
    foreach ($c in @(
        (Join-Path $WinRoot 'src\TipCL\bin\Release\net48\TipCL.exe'),
        (Join-Path $WinRoot 'src\TipCL\bin\Debug\net48\TipCL.exe'))) {
        if (Test-Path $c) { $Exe = $c; break }
    }
}
if (-not $Exe -or -not (Test-Path $Exe)) {
    throw "找不到 TipCL.exe —— 先构建: dotnet build windos/src/TipCL/TipCL.csproj -c Release"
}
$Exe = (Resolve-Path $Exe).Path

if (-not $OutDir) { $OutDir = Join-Path $RepoRoot 'dist\wpf-smoke-matrix' }
$OutDir = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# 每次跑一份全新隔离根(绝不能落到 %LOCALAPPDATA%\TipCL)
$homeDir = Join-Path $OutDir ('home-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $homeDir | Out-Null
$resolvedHome = [IO.Path]::GetFullPath($homeDir)
if (-not $resolvedHome.StartsWith($OutDir, [StringComparison]::OrdinalIgnoreCase)) {
    throw ("拒绝运行: TIPCL_HOME '{0}' 不在 -OutDir '{1}' 之内" -f $resolvedHome, $OutDir)
}

function Write-Utf8NoBom {
    param([string]$Path, [string]$Text)
    [IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

# 首启/反馈同意预置:免得首启对话框抢在前面(那条由 smoke 自己也会处理,但预置更稳)
Write-Utf8NoBom -Path (Join-Path $homeDir 'config.json') -Text '{"first_run": false, "feedback_consent": true}'

Write-Host 'TipCL WPF 冒烟矩阵(真开窗口 / 每页截图 / 每次点击 / 每个对话框)'
Write-Host ("  exe   : {0}" -f $Exe)
Write-Host ("  home  : {0}" -f $homeDir)
Write-Host ("  lang  : {0}" -f $Lang)
Write-Host ''

$sw = [Diagnostics.Stopwatch]::StartNew()
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $Exe
$psi.Arguments = '--smoke'
$psi.WorkingDirectory = $homeDir
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.CreateNoWindow = $true
# 语言跟着隔离根走(产品的语言配置在 config.json / ui_lang;smoke 读 I18n.Current)
$psi.EnvironmentVariables['TIPCL_HOME'] = $homeDir
$psi.EnvironmentVariables['TEMP'] = $homeDir
$psi.EnvironmentVariables['TMP'] = $homeDir

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
Write-Utf8NoBom -Path (Join-Path $homeDir 'console.log') -Text ($stdout + "`n" + $stderr)

# ---- 找这一轮的 report.json(取最新一份 run-*/report.json)
$smokeRoot = Join-Path $homeDir '_wpf_smoke'
$report = $null
$runDir = $null
if (Test-Path $smokeRoot) {
    $cand = Get-ChildItem $smokeRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($cand) {
        $runDir = $cand.FullName
        $p = Join-Path $runDir 'report.json'
        if (Test-Path $p) {
            try { $report = Get-Content $p -Raw -Encoding UTF8 | ConvertFrom-Json } catch { $report = $null }
        }
    }
}

$problems = New-Object System.Collections.ArrayList
function Fail([string]$m) { $problems.Add($m) | Out-Null }

# ---- C1 / C9
if ($timedOut) { Fail ("C1 进程超时({0}s)被强杀" -f $TimeoutSec) }
elseif ($code -ne 0) { Fail ("C1 退出码 {0}(0 才算过;99 = smoke 自己的 watchdog 超时)" -f $code) }
if ($code -eq 99) { Fail 'C9 watchdog 超时(state=watchdog-timeout)' }

# ---- C2
if (-not $report) {
    Fail ("C2 没找到可解析的 report.json(期望在 {0}\_wpf_smoke\*\report.json)" -f $homeDir)
} else {
    if ($report.state -ne 'finished') { Fail ("C2 state={0}(应为 finished)" -f $report.state) }
    if ($report.state -eq 'watchdog-timeout') { Fail 'C9 state=watchdog-timeout' }
}

$pageRows = New-Object System.Collections.ArrayList
$clickRows = New-Object System.Collections.ArrayList

if ($report) {
    # ---- C3
    $issueN = 0
    if ($report.issues) { $issueN = @($report.issues).Count }
    if ($issueN -gt 0) {
        Fail ("C3 issues {0} 条: {1}" -f $issueN, ((@($report.issues) | Select-Object -First 5) -join ' | '))
    }

    # ---- C4 + C7
    $pages = @($report.pages)
    if ($pages.Count -lt $MinPages) { Fail ("C4 只覆盖 {0} 页(< {1})" -f $pages.Count, $MinPages) }
    foreach ($pg in $pages) {
        $shot = $pg.shot
        $shotBytes = 0
        if ($shot -and (Test-Path $shot)) { $shotBytes = (Get-Item $shot).Length }
        $pageRows.Add([pscustomobject]@{
                Page = $pg.id; Title = $pg.title; Ok = [bool]$pg.ok; Ms = $pg.ms; ShotBytes = $shotBytes; Shot = $shot
            }) | Out-Null
        if (-not $pg.ok) { Fail ("C4 页 '{0}' ok=false" -f $pg.id) }
        if (-not $shot -or $shot -like '截图失败*') { Fail ("C7 页 '{0}' 没有截图({1})" -f $pg.id, $shot) }
        elseif ($shotBytes -le $MinShotBytes) { Fail ("C7 页 '{0}' 截图只有 {1} B(<= {2})" -f $pg.id, $shotBytes, $MinShotBytes) }
    }

    # ---- C5
    $clicks = @($report.clicks)
    $requiredClicks = [int]$report.required_clicks
    if ($clicks.Count -lt $requiredClicks) {
        Fail ("C5 只点了 {0} 次(< required_clicks {1})" -f $clicks.Count, $requiredClicks)
    }
    foreach ($c in $clicks) {
        $clickRows.Add([pscustomobject]@{ Page = $c.page; Label = $c.label; Ok = [bool]$c.ok; Ms = $c.ms }) | Out-Null
        if (-not $c.ok) { Fail ("C5 点击 '{0}' / '{1}' ok=false" -f $c.page, $c.label) }
    }

    # ---- C6
    $dialogs = @($report.dialogs)
    $requiredDialogs = [int]$report.required_dialogs
    if ($dialogs.Count -lt $requiredDialogs) {
        Fail ("C6 只开了 {0} 个对话框(< required_dialogs {1})" -f $dialogs.Count, $requiredDialogs)
    }

    # ---- C8
    $missing = @($report.i18n_missing)
    if ($missing.Count -gt 0) {
        Fail ("C8 i18n_missing {0} 条: {1}" -f $missing.Count, (($missing | Select-Object -First 5) -join ' | '))
    }
}

Write-Host '== 页面 =='
if ($pageRows.Count -gt 0) { $pageRows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host }
Write-Host '== 点击 =='
if ($clickRows.Count -gt 0) { $clickRows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host }
if ($report) {
    Write-Host ("state={0} exit={1} pages={2} clicks={3}/>={4} dialogs={5}/>={6} elapsed={7}ms bridge={8}" -f `
            $report.state, $code, @($report.pages).Count, @($report.clicks).Count, $report.required_clicks,
        @($report.dialogs).Count, $report.required_dialogs, $report.elapsed_ms, $report.bridge)
}

if ($Markdown) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('| 页面 | 标题 | 结论 | 耗时 (ms) | 截图 (B) |')
    [void]$sb.AppendLine('| --- | --- | --- | --- | --- |')
    foreach ($r in $pageRows) {
        [void]$sb.AppendLine(("| {0} | {1} | {2} | {3} | {4} |" -f $r.Page, $r.Title, $(if ($r.Ok) { 'OK' } else { 'FAIL' }), $r.Ms, $r.ShotBytes))
    }
    $mdPath = if ([IO.Path]::IsPathRooted($Markdown)) { $Markdown } else { Join-Path (Get-Location).Path $Markdown }
    $mdPath = [IO.Path]::GetFullPath($mdPath)
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $mdPath) | Out-Null
    Write-Utf8NoBom -Path $mdPath -Text $sb.ToString()
    Write-Host ("wrote {0}" -f $mdPath)
}

Write-Host ''
if ($problems.Count -gt 0) {
    Write-Host ("FAILURES ({0}):" -f $problems.Count) -ForegroundColor Red
    foreach ($p in $problems) { Write-Host ("  - {0}" -f $p) -ForegroundColor Red }
    Write-Host ("现场保留: {0}" -f $homeDir)
    if (Test-Path (Join-Path $homeDir 'console.log')) {
        Write-Host '    --- console(尾部 20 行)---'
        Get-Content (Join-Path $homeDir 'console.log') -Tail 20 | ForEach-Object { Write-Host ("    " + $_) }
    }
    exit 1
}

Write-Host ("WPF 冒烟矩阵全部通过(9 条判据):{0} 页 / {1} 次点击 / {2} 个对话框 / issues 0 / 总耗时 {3} ms" -f `
        @($report.pages).Count, @($report.clicks).Count, @($report.dialogs).Count, $sw.ElapsedMilliseconds) -ForegroundColor Green
if ($report) { Write-Host ("report: {0}" -f (Join-Path $runDir 'report.json')) }
if ($Clean) {
    # 只有显式 -Clean 才删隔离根:默认保留,因为 report.json 与截图是**唯一的证据**,
    # 而且它是判据测试(Set-Item env:TIPCL_WPF_SMOKE_REPORT)的输入。
    try { Remove-Item $homeDir -Recurse -Force -ErrorAction SilentlyContinue } catch { }
}
exit 0