# tools/build.ps1 — TipCL Linux 版一键构建（M1-A5 充实）
# 用法:
#   powershell -ExecutionPolicy Bypass -File tools/build.ps1                    # 只构建
#   powershell -ExecutionPolicy Bypass -File tools/build.ps1 -Test              # 构建 + 测试
#   powershell -ExecutionPolicy Bypass -File tools/build.ps1 -Test -Smoke       # 构建 + 测试 + 冒烟(--smoke 出 smoke.png)
#   可选 -Configuration Debug（默认 Release）
#
# 防锁依据 GOAL §6.4: build-server shutdown + -p:UseSharedCompilation=false + -m:1；
# CS2012 文件锁时 shutdown 等 10 秒重试（最多 3 次），逻辑沿用 Windows 版 tools/build-both.ps1（D-14）。
param(
    [string]$Configuration = "Release",
    [switch]$Test,
    [switch]$Smoke
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Shutdown-BuildServers {
    dotnet build-server shutdown | Out-Null
}

function Invoke-DotnetBuild {
    $attempt = 0
    while ($true) {
        $attempt++
        $output = & dotnet build TipCL.Linux.sln -c $Configuration `
            -p:UseSharedCompilation=false -m:1 2>&1
        $output | ForEach-Object { Write-Host $_ }
        $failed = ($LASTEXITCODE -ne 0) -or ($output | Where-Object { $_ -match 'error\s+CS' })
        if (-not $failed) { break }
        if ($output -match 'CS2012' -and $attempt -le 3) {
            Write-Warning "检测到 CS2012 文件锁（可能与其他并行构建冲突），shutdown 后等 10 秒重试（第 $attempt 次）..."
            Shutdown-BuildServers
            Start-Sleep -Seconds 10
            continue
        }
        Write-Error "构建失败（退出码 $LASTEXITCODE 或存在 error CS）。"
        exit 1
    }
    Write-Host "构建完成: 0 错误 ($Configuration)" -ForegroundColor Green
}

Write-Host "===== 构建 ($Configuration) =====" -ForegroundColor Cyan
Shutdown-BuildServers
Invoke-DotnetBuild

if ($Test) {
    Write-Host "`n===== 测试 =====" -ForegroundColor Cyan
    Shutdown-BuildServers
    dotnet test TipCL.Linux.sln -c $Configuration --no-build `
        -p:UseSharedCompilation=false -m:1
    if ($LASTEXITCODE -ne 0) { Write-Error "dotnet test 失败。"; exit $LASTEXITCODE }
}

if ($Smoke) {
    Write-Host "`n===== 冒烟 (--smoke) =====" -ForegroundColor Cyan
    $smokePng = Join-Path $root "smoke.png"
    if (Test-Path $smokePng) { Remove-Item $smokePng -Force }
    Push-Location $root
    try {
        dotnet run --project src/TipCL.App -c $Configuration --no-build -- --smoke
        if ($LASTEXITCODE -ne 0) { Write-Error "--smoke 退出码 $LASTEXITCODE。"; exit $LASTEXITCODE }
    } finally {
        Pop-Location
    }
    if (-not (Test-Path $smokePng)) {
        Write-Error "冒烟未产出 $smokePng（应人工核对窗口外观: 启动器外壳 1180x740）"
        exit 1
    }
    $bytes = (Get-Item $smokePng).Length
    if ($bytes -lt 1024) {
        Write-Error "smoke.png 仅 $bytes B，疑似空白渲染，请人工核对。"
        exit 1
    }
    Write-Host "冒烟通过: $smokePng ($bytes B)" -ForegroundColor Green
}

Write-Host "`n全部完成。" -ForegroundColor Green
exit 0
