# tools/build.ps1 — TipCL Linux 版一键构建（M1-A1 骨架，A5 充实）
# 用法: powershell -ExecutionPolicy Bypass -File tools/build.ps1 [-Configuration Release] [-Test] [-Smoke]
param(
    [string]$Configuration = "Release",
    [switch]$Test,
    [switch]$Smoke
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# 构建防锁（GOAL §6.4）
dotnet build-server shutdown

dotnet build TipCL.Linux.sln -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($Test) {
    dotnet test TipCL.Linux.sln -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($Smoke) {
    dotnet run --project src/TipCL.App -c $Configuration --no-build -- --smoke
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "构建完成 ($Configuration)"
