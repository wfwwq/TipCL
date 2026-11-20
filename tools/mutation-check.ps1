# tools/mutation-check.ps1 —— 手工版变异测试(audit/D 第 7 轮)
#
# 目的:证明**测试真的能抓到 bug**,而不是"跑绿了就算覆盖了"。做法:把某个修复点**改回 bug 形态**
# (最小改动、只改一处),重跑相关测试 —— 必须变红;然后 `git checkout` 还原。
#
# 每个变异点记录:改名 / 改回方式 / 有没有变红 / 红了哪条 / 耗时。**没变红的就是覆盖漏洞**。
#
# 安全纪律:每个变异点前后都 `git checkout -- <file>`,并在 finally 里再还原一次;
# 脚本开头检查工作区是否干净(不干净直接拒绝跑,免得把变异提交进去)。
#
# 用法(仓库根):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\mutation-check.ps1
#   ... -Only PyJson_MaxDepth,Cleaner_Contract      # 只跑指定的
#   ... -Markdown progress/MUTATION-REPORT.md

param(
    [string]$Root,
    [string[]]$Only,
    [string]$Markdown,
    [switch]$KeepGoing
)

$ErrorActionPreference = 'Stop'

if (-not $Root) {
    $Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
}
$Root = [IO.Path]::GetFullPath($Root)
Push-Location $Root

$linuxTests = 'tests\TipCL.Tests\TipCL.Tests.csproj'
$winTests = 'windos\tests\TipCL.Tests\TipCL.Tests.csproj'

# ────────────────────────────── 变异点表 ──────────────────────────────
# Kind=text:按字面量替换(Occurrences 必须是 1);Kind=regex:按正则替换(默认只替第一处)
$Mutations = @(
    @{ Name = 'PyJson_MaxDepth'
       File = 'src/TipCL.Core/PyJson.cs'; Kind = 'text'
       Old = 'private const int MaxDepth = 512;'; New = 'private const int MaxDepth = int.MaxValue;'
       Suite = 'linux'; Filter = 'FullyQualifiedName~PyJsonMalformedTests'
       Expect = 'PyJson_RejectsPathologicalNestingWithACatchableError / DepthLimitAlsoGuardsMixedNesting' }

    @{ Name = 'PyJson_MatchTruncated'
       File = 'src/TipCL.Core/PyJson.cs'; Kind = 'text'
       Old = 'if (string.CompareOrdinal(_s, i, w, 0, w.Length) == 0)'
       New = 'if (string.CompareOrdinal(_s.Substring(i, w.Length), w) == 0)'
       Suite = 'linux'; Filter = 'FullyQualifiedName~PyJsonMalformedTests'
       Expect = 'LoadsOr 对截断关键字(nul/tru/fals)回落默认值' }

    @{ Name = 'Mods_ContentDirIgnoresIsolation'
       File = 'src/TipCL.Core/Launcher/Mods.cs'; Kind = 'text'
       Old = 'return PPath.Join(VersionSettings.GameDir(instance, version_id), subdir);'
       New = 'return PPath.Join(instance.path, subdir);'
       Suite = 'linux'; Filter = 'FullyQualifiedName~CoreHardeningTests'
       Expect = 'Mods_ContentDirFollowsPerVersionGameDirWhenIsolated' }

    @{ Name = 'Cleaner_DropsFailedKeys'
       File = 'src/TipCL.Core/Launcher/Cleaner.cs'; Kind = 'text'
       Old = 'return PyDict.Of("removed", num, "bytes", num2, "failed", num3, "error", firstError ?? "");'
       New = 'return PyDict.Of("removed", num, "bytes", num2);'
       Suite = 'linux'; Filter = 'FullyQualifiedName~CoreHardeningTests'
       Expect = 'Cleaner_ApplyAlwaysReportsTheFourContractKeys' }

    @{ Name = 'LayoutStoreV2_NoBackup'
       File = 'src/TipCL.Core/Launcher/Layout/LayoutStoreV2.cs'; Kind = 'text'
       Old = '_pendingCorruptBackup = v2Path;'
       New = '_pendingCorruptBackup = "";'
       Suite = 'linux'; Filter = 'FullyQualifiedName~CoreHardeningTests'
       Occurrences = 2
       Expect = 'LayoutStoreV2_BacksUpCorruptFileBeforeOverwritingIt' }

    @{ Name = 'AiAgent_NoTextRollback'
       File = 'src/TipCL.Core/Launcher/Ai/AiAgent.cs'; Kind = 'text'
       Old = 'state.Text.Length = textBase;'; New = '/* MUTANT: 不回滚半截文本 */;'
       Suite = 'linux'; Filter = 'FullyQualifiedName~AiRetryTests'
       Expect = '重试后 state.Text 不应重复前半段' }

    @{ Name = 'AiPage_EmptyStateAlwaysAdd'
       File = 'src/TipCL.App/Pages/Ai/AiPage.cs'; Kind = 'text'
       Old = 'if (!_messages.Children.Contains(_empty))'; New = 'if (true)'
       Suite = 'linux'; Filter = 'FullyQualifiedName~TipCL.Tests.App.AiPage'
       Expect = '清空/新对话重复挂空态 → InvalidOperationException' }

    @{ Name = 'Launch_GUARD_Disabled_Linux'
       File = 'src/TipCL.App/Pages/Launch/LaunchPage.cs'; Kind = 'text'
       Old = 'if (_launching)'; New = 'if (false && _launching)'
       Suite = 'linux'; Filter = 'FullyQualifiedName~TipCL.Tests.App'
       Expect = '连点启动按钮 → 只起一次' }

    @{ Name = 'GridCanvasPanel_NoRemeasureOnWidthChange'
       File = 'src/TipCL.App/Pages/Layout/GridCanvasPanel.cs'; Kind = 'text'
       Old = 'else if (Math.Abs(width - _measuredWidth) > 0.5)'; New = 'else if (false)'
       Suite = 'linux'; Filter = 'FullyQualifiedName~Layout'
       Expect = 'Arrange 宽 ≠ 测量宽 → 行高重算(滚动条出现)' }

    @{ Name = 'Java_Chmod755NoOp'
       File = 'src/TipCL.Core/Launcher/Java.cs'; Kind = 'text'
       Old = 'if (string.IsNullOrEmpty(p))'; New = 'if (true)'
       Suite = 'linux'; Filter = 'FullyQualifiedName!~TipCL.Tests.App'
       Expect = '解压后的 java 必须可执行(仅 Linux 可判)' }

    @{ Name = 'MutateAsync_SwallowsFailure'
       File = 'windos/src/TipCL/Services/BridgeClient.cs'; Kind = 'text'
       Old = 'await CallAsync<object>(method, args).ConfigureAwait(continueOnCapturedContext: false);'
       New = 'try { await CallAsync<object>(method, args).ConfigureAwait(false); } catch { } // MUTANT'
       Suite = 'win'; Filter = 'FullyQualifiedName~SilentMutationGuardTests'
       Expect = 'MutateAsync 失败必须返回 false(而不是吞掉)' }

    @{ Name = 'StackDragPlan_NoHysteresis'
       File = 'windos/src/TipCL/Pages/Layout/StackDragPlan.cs'; Kind = 'text'
       Old = 'public const double Hysteresis = 6.0;'; New = 'public const double Hysteresis = 0.0;'
       Suite = 'win'; Filter = 'FullyQualifiedName~StackDragPlan'
       Expect = '指针停在中点附近不应来回跳(滞回)' }

    @{ Name = 'Launch_GUARD_Disabled_Windows'
       File = 'windos/src/TipCL/Pages/LaunchPage.cs'; Kind = 'text'
       Old = 'if (_launching)'; New = 'if (false && _launching)'
       Suite = 'win'; Filter = 'FullyQualifiedName~Launch'
       Expect = 'Windows 侧连点启动只起一次' }
)

# ─────────────────────────────── 前置检查 ───────────────────────────────
$dirty = git status --porcelain | Where-Object { $_ -notmatch '^\?\?' }   # 未跟踪的新文件不影响变异(checkout 不会碰它们)
if ($dirty) {
    Write-Host '工作区不干净,拒绝跑变异(变异会混进未提交改动里):' -ForegroundColor Red
    $dirty | ForEach-Object { Write-Host ('  ' + $_) }
    Pop-Location
    exit 2
}

function Invoke-Dotnet {
    param([string[]]$Args2, [int]$TimeoutSec = 900)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'dotnet'
    $psi.Arguments = ($Args2 | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $psi.WorkingDirectory = $Root
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $o = $p.StandardOutput.ReadToEndAsync()
    $e = $p.StandardError.ReadToEndAsync()
    if (-not $p.WaitForExit($TimeoutSec * 1000)) { try { $p.Kill() } catch { } }
    $p.WaitForExit()
    return @{ Code = $p.ExitCode; Out = ($o.Result + "`n" + $e.Result) }
}

$rows = New-Object System.Collections.ArrayList

foreach ($m in $Mutations) {
    if ($Only -and $Only.Count -gt 0 -and ($Only -notcontains $m.Name)) { continue }

    $file = Join-Path $Root $m.File
    $rel = $m.File
    Write-Host ''
    Write-Host ("=== 变异: {0}  ({1}) ===" -f $m.Name, $rel) -ForegroundColor Cyan

    $row = [ordered]@{
        Mutation = $m.Name; File = $rel; Patch = 'no'; Build = 'no'; Red = 'no'
        Failures = ''; Ms = 0; Note = ''
    }
    $sw = [Diagnostics.Stopwatch]::StartNew()

    try {
        git checkout -- $rel
        $text = [IO.File]::ReadAllText($file)
        $count = ([regex]::Matches($text, [regex]::Escape($m.Old))).Count
        if ($count -eq 0) {
            $row.Patch = 'MISS'; $row.Note = '锚点没找到(代码变了?)'
            Write-Host '  锚点没找到,跳过' -ForegroundColor Yellow
            $rows.Add([pscustomobject]$row) | Out-Null
            continue
        }
        $want = if ($m.ContainsKey('Occurrences')) { [int]$m.Occurrences } else { 1 }
        if ($count -ne $want) {
            $row.Patch = 'AMBIGUOUS'; $row.Note = ("锚点出现 {0} 次,期望 {1} 次" -f $count, $want)
            Write-Host ("  锚点出现 {0} 次(期望 {1}),跳过" -f $count, $want) -ForegroundColor Yellow
            $rows.Add([pscustomobject]$row) | Out-Null
            continue
        }

        $mutated = $text.Replace($m.Old, $m.New)
        [IO.File]::WriteAllText($file, $mutated, (New-Object System.Text.UTF8Encoding($false)))
        $row.Patch = 'yes'
        Write-Host ('  已改回 bug 形态:{0} → {1}' -f $m.Old.Substring(0, [Math]::Min(60, $m.Old.Length)), $m.New.Substring(0, [Math]::Min(60, $m.New.Length)))

        $proj = if ($m.Suite -eq 'win') { $winTests } else { $linuxTests }
        $b = Invoke-Dotnet -Args2 @('build', $proj, '--no-restore', '-v', 'q', '--nologo')
        $row.Build = if ($b.Code -eq 0) { 'ok' } else { 'FAIL' }
        if ($b.Code -ne 0) {
            $row.Note = '变异后编译不过(变异本身非法)'
            ($b.Out -split "`n" | Where-Object { $_ -match 'error' } | Select-Object -First 2) | ForEach-Object { Write-Host ('    ' + $_.Trim()) -ForegroundColor Yellow }
        }
        else {
            $t = Invoke-Dotnet -Args2 @('test', $proj, '--no-build', '--nologo', '--filter', $m.Filter)
            $fails = @()
            foreach ($line in ($t.Out -split "`n")) {
                if ($line -match '\[FAIL\]') {
                    $fails += (($line -replace '.*\]\s+', '') -replace '\s*\[FAIL\].*', '').Trim()
                }
            }
            $row.Red = if ($t.Code -ne 0) { 'yes' } else { 'NO' }
            $row.Failures = ($fails | Select-Object -Unique) -join ' | '
            if ($t.Code -eq 0) {
                Write-Host '  ❗ 测试仍然全绿 —— 覆盖漏洞(见报告)' -ForegroundColor Red
            }
            else {
                Write-Host ('  变红:{0}' -f $row.Failures) -ForegroundColor Green
            }
        }
    }
    catch {
        $row.Note = $_.Exception.Message
        Write-Host ('  异常:' + $_.Exception.Message) -ForegroundColor Yellow
    }
    finally {
        git checkout -- $rel 2>$null | Out-Null
        $sw.Stop()
        $row.Ms = $sw.ElapsedMilliseconds
    }

    $rows.Add([pscustomobject]$row) | Out-Null
    if (-not $KeepGoing -and $row.Red -eq 'NO' -and $Only -eq $null) {
        # 默认继续跑完:覆盖漏洞本身就是要收集的产出
    }
}

# ─────────────────────────────── 汇总 ───────────────────────────────
Write-Host ''
Write-Host '== 变异测试结果 =='
$rows | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Host

$tsv = Join-Path $Root 'dist\mutation-check.tsv'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $tsv) | Out-Null
$rows | Export-Csv -NoTypeInformation -Encoding UTF8 -Delimiter "`t" $tsv
Write-Host ("wrote {0}" -f $tsv)

if ($Markdown) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('| # | 变异点 | 文件 | 改回 | 红? | 红了哪条 | 耗时 (ms) |')
    [void]$sb.AppendLine('| --- | --- | --- | --- | --- | --- | --- |')
    $i = 0
    foreach ($r in $rows) {
        $i++
        $verdict = if ($r.Red -eq 'yes') { 'YES' } elseif ($r.Red -eq 'NO') { '**NO(漏洞)**' } else { $r.Patch }
        [void]$sb.AppendLine(("| {0} | {1} | `{2}` | 锚点替换 | {3} | {4} | {5} |" -f $i, $r.Mutation, $r.File, $verdict, ($r.Failures -replace '\|', '/'), $r.Ms))
    }
    $mdPath = if ([IO.Path]::IsPathRooted($Markdown)) { $Markdown } else { Join-Path $Root $Markdown }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $mdPath) | Out-Null
    [IO.File]::WriteAllText($mdPath, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("wrote {0}" -f $mdPath)
}

# 收尾:确认工作区已还原
$left = git status --porcelain
if ($left) {
    Write-Host '警告:跑完后工作区仍有改动,请人工检查:' -ForegroundColor Red
    $left | ForEach-Object { Write-Host ('  ' + $_) }
    Pop-Location
    exit 3
}

$holes = @($rows | Where-Object { $_.Red -eq 'NO' })
Pop-Location
if ($holes.Count -gt 0) {
    Write-Host ("完成:{0} 个变异点里 {1} 个测试没抓到(见上表)" -f $rows.Count, $holes.Count) -ForegroundColor Yellow
    exit 1
}
Write-Host ("完成:{0} 个变异点全部被测试抓到" -f $rows.Count) -ForegroundColor Green
exit 0