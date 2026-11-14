# pack-core-resources.ps1 —— 把 Core 内嵌资源(locales / ai / plugin-host)确定性 gzip 成同目录同名 .gz
#
# 用法(仓库根或任意位置均可):
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\pack-core-resources.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\pack-core-resources.ps1 -Check
#
# 为什么需要它:src/TipCL/TipCL.csproj 的 4 条 EmbeddedResource 指向 .gz(LogicalName 不变),
# exe 因此少 387,539 B(≈378.5 KB)。读取侧的解压收口在 src/TipCL/Core/CoreResources.cs。
#
# 确定性:固定 gzip level 9 且不写 mtime/.NET GZipStream 只写 MTIME=0,同一输入必得同一字节,
# 所以重复运行不会产生 diff。
# -Check:只校验不写盘,发现 .gz 与源不一致(缺失或内容过期)时 exit 1 —— 供构建前守卫使用。

[CmdletBinding()]
param(
    [switch]$Check,
    [string]$Repo
)

$ErrorActionPreference = 'Stop'

if (-not $Repo) {
    $Repo = Split-Path -Parent $PSScriptRoot
}
$resRoot = Join-Path $Repo 'src\TipCL\Resources'
if (-not (Test-Path $resRoot)) { throw "未找到资源根: $resRoot" }

# 只处理这三类(与 csproj 里被 gzip 内嵌的资源一一对应)
$groups = @(
    'locales\en.json',
    'locales\zh_CN.json',
    'ai\model_rules.json'
)
$phRoot = Join-Path $resRoot 'plugin-host'
foreach ($f in (Get-ChildItem $phRoot -Recurse -File | Sort-Object FullName)) {
    # 排除上一步的产物:否则第二次运行会把 .gz 再压一遍(源集合自我增长)
    if ($f.Name.EndsWith('.gz')) { continue }
    $groups += ('plugin-host\' + $f.FullName.Substring($phRoot.Length + 1))
}

function Compress-Gzip {
    param([byte[]]$Bytes)
    $ms = New-Object System.IO.MemoryStream
    # leaveOpen:$true 以便随后取 ToArray()
    $gz = New-Object System.IO.Compression.GZipStream($ms, [System.IO.Compression.CompressionLevel]::Optimal, $true)
    try {
        $gz.Write($Bytes, 0, $Bytes.Length)
    } finally {
        $gz.Dispose()
    }
    return $ms.ToArray()
}

$stale = New-Object System.Collections.Generic.List[string]
$raw = 0; $packed = 0

foreach ($rel in $groups) {
    $src = Join-Path $resRoot $rel
    if (-not (Test-Path $src)) { throw "缺少源文件: $src" }
    $dst = "$src.gz"
    $srcBytes = [System.IO.File]::ReadAllBytes($src)
    $newBytes = Compress-Gzip -Bytes $srcBytes

    $raw += $srcBytes.Length
    $packed += $newBytes.Length

    $isCurrent = $false
    if (Test-Path $dst) {
        $old = [System.IO.File]::ReadAllBytes($dst)
        if ($old.Length -eq $newBytes.Length) {
            $same = $true
            for ($i = 0; $i -lt $old.Length; $i++) { if ($old[$i] -ne $newBytes[$i]) { $same = $false; break } }
            $isCurrent = $same
        }
    }

    if ($isCurrent) {
        Write-Host ("  ok   {0}  {1} -> {2} B" -f $rel, $srcBytes.Length, $newBytes.Length)
    } else {
        if ($Check) {
            $stale.Add($rel)
            Write-Host ("  STALE {0}" -f $rel) -ForegroundColor Red
        } else {
            [System.IO.File]::WriteAllBytes($dst, $newBytes)
            Write-Host ("  pack {0}  {1} -> {2} B" -f $rel, $srcBytes.Length, $newBytes.Length)
        }
    }
}

Write-Host ("`n源 {0} B / 压缩后 {1} B / 可省 {2} B" -f $raw, $packed, ($raw - $packed))

if ($Check -and $stale.Count -gt 0) {
    Write-Error ("以下 .gz 缺失或已过期,请先运行 tools/pack-core-resources.ps1 : " + ($stale -join ', '))
    exit 1
}
Write-Host '完成。'
