# tools/build-both.ps1 — TipCL compact / premium 一键双版构建脚本（WP-S1）
# 用法:
#   .\tools\build-both.ps1              # 默认 -Both: 先 compact 后 premium
#   .\tools\build-both.ps1 -Premium     # 只构 premium
#   .\tools\build-both.ps1 -Compact     # 只构 compact
#   .\tools\build-both.ps1 -Both -Dist  # 构建后复制到 ..\dist\ 并输出 SHA256（dist 正式重切由监工做，默认不开）
#
# 依据: DECISIONS.md D-14（标准构建防锁参数）、D-19（compact 红线 5,757,264 B）、D-18（dist 由监工统一重切）
# 坑依据: progress/WP-U.md —— premium 构建后紧接 csproj 增量构建会把 exe 顶高 +512 B（残留），
#         因此本脚本每次构建前删除 bin/obj 强制全量，等效 Rebuild，验收以全量构建为准。

[CmdletBinding()]
param(
    [switch]$Premium,
    [switch]$Compact,
    [switch]$Both,
    [switch]$Dist
)

$ErrorActionPreference = 'Stop'
$Repo = Split-Path -Parent $PSScriptRoot   # 仓库根（tools/ 的上级）
Set-Location $Repo

# compact 体积红线（DECISIONS D-19：5,707,264 + 50 KB）
$CompactLimit = 5757264

# 决定要构建哪些 flavor
$flavors = @()
if ($Both -or (-not $Premium -and -not $Compact)) { $flavors = @('compact', 'premium') }
elseif ($Premium) { $flavors = @('premium') }
else { $flavors = @('compact') }

function Shutdown-BuildServers {
    # D-14: VBCSCompiler / 悬空 dotnet host 反复锁 obj（CS2012），先关掉所有 build server
    dotnet build-server shutdown | Out-Null
}

function Remove-BinObj {
    # D-14 / WP-U 坑: 陈旧或跨 flavor 的增量产物会残留（premium 后接 compact 增量曾 +512 B 顶线），
    # 必须 rm 净 bin/obj 强制全量构建
    foreach ($d in @('src\TipCL\bin', 'src\TipCL\obj')) {
        $p = Join-Path $Repo $d
        if (Test-Path $p) { Remove-Item -Recurse -Force $p }
    }
}

function Invoke-BuildFlavor {
    param([string]$Name)

    Write-Host "`n===== 构建 $Name =====" -ForegroundColor Cyan
    Shutdown-BuildServers
    Remove-BinObj

    # D-14 标准参数:
    #   -c Release                       — 交付配置
    #   -p:UseSharedCompilation=false    — 不用 VBCSCompiler 共享编译进程，杜绝其锁 obj（CS2012）
    #   -p:DebugType=none                — 不产出 pdb（D-14: 使 exe 再 -512 B）
    #   -p:DebugSymbols=false            — 不嵌调试符号，与上一条配套
    #   -m:1                             — 单进程构建，避免并行节点争抢同一 obj
    #   premium 额外 -p:Premium=true     — D-6/交接必读 2.5: 同一代码树 + 编译开关双 flavor
    $args = @('build', 'TipCL.sln', '-c', 'Release',
        '-p:UseSharedCompilation=false', '-p:DebugType=none', '-p:DebugSymbols=false', '-m:1')
    if ($Name -eq 'premium') { $args += '-p:Premium=true' }

    # 遇 CS2012（并行代理如 WP-N 同抢 obj）: shutdown + 等 10 秒重试，最多 3 次
    $attempt = 0
    while ($true) {
        $attempt++
        $output = & dotnet @args 2>&1
        $output | ForEach-Object { Write-Host $_ }
        $failed = ($LASTEXITCODE -ne 0) -or ($output | Where-Object { $_ -match 'error\s+CS' })
        if (-not $failed) { break }
        if ($output -match 'CS2012' -and $attempt -le 3) {
            Write-Warning "检测到 CS2012 文件锁（可能与并行构建冲突），shutdown 后等 10 秒重试（第 $attempt 次）..."
            Shutdown-BuildServers
            Start-Sleep -Seconds 10
            Remove-BinObj
            continue
        }
        Write-Error "$Name 构建失败（错误非零或有 error CS），退出。"
        exit 1
    }
    Write-Host "$Name 构建: 0 错误" -ForegroundColor Green

    $exe = Join-Path $Repo 'src\TipCL\bin\Release\net48\TipCL.exe'
    if (-not (Test-Path $exe)) { Write-Error "未找到 $exe"; exit 1 }
    $bytes = (Get-Item $exe).Length
    Write-Host "$Name exe: $bytes B"

    if ($Name -eq 'compact') {
        # D-19: compact 红线 5,757,264 B，超限即失败（premium 只记录不设限）
        if ($bytes -gt $CompactLimit) {
            Write-Error ("compact 超红线: {0} B > 限制 {1} B，超出 {2} B" -f $bytes, $CompactLimit, ($bytes - $CompactLimit))
            exit 1
        }
        Write-Host ("compact 合规: 余量 {0} B" -f ($CompactLimit - $bytes)) -ForegroundColor Green
    }

    # -Dist 修复（WP-S5）: premium 后构建会覆盖 bin，必须在此处（每个 flavor 构建完立即）
    # 复制并算哈希，不能等全部构建完再统一复制（否则 compact 副本错拿 premium）
    $distSha = $null
    if ($Dist) {
        $distDir = 'D:\项目\tipcl\windos\dist'
        New-Item -ItemType Directory -Force -Path $distDir | Out-Null
        $dest = Join-Path $distDir ("TipCL-{0}.exe" -f $Name)
        Copy-Item $exe $dest -Force
        $distSha = (Get-FileHash $dest -Algorithm SHA256).Hash
        Write-Host ("dist: {0}  {1} B  SHA256 {2}" -f $dest, $bytes, $distSha) -ForegroundColor Green
    }

    return @{ Name = $Name; Exe = $exe; Bytes = $bytes; DistSha = $distSha }
}

$results = @()
foreach ($f in $flavors) { $results += Invoke-BuildFlavor -Name $f }

Write-Host "`n===== 汇总 =====" -ForegroundColor Cyan
foreach ($r in $results) {
    Write-Host ("{0,-8} {1,12} B  {2}" -f $r.Name, $r.Bytes, $r.Exe)
}

if ($Dist) {
    # D-18: dist 正式重切由监工统一做；本开关复制+哈希已在各 flavor 构建完立即完成（WP-S5 修复）
    # 此处只汇总复核 dist 文件实际哈希，防止再出张冠李戴
    foreach ($r in $results) {
        $dest = Join-Path 'D:\项目\tipcl\windos\dist' ("TipCL-{0}.exe" -f $r.Name)
        $sha = (Get-FileHash $dest -Algorithm SHA256).Hash
        Write-Host ("dist 复核: {0}  SHA256 {1}" -f $dest, $sha)
    }
}

Write-Host "`n全部完成。" -ForegroundColor Green
exit 0
