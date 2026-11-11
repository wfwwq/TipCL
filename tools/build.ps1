# TipCL.Mac 构建脚本(A1 建立)
#
# 构建防锁(见 GOAL.md 第 4 节):先停掉 build server,再串行(-m:1)、
# 关共享编译(-p:UseSharedCompilation=false)地构建,输出只看错误摘要。
#
# 用法:
#   pwsh -File tools/build.ps1                 # Release 构建
#   pwsh -File tools/build.ps1 -Test           # 构建后跑 dotnet test
#   pwsh -File tools/build.ps1 -Configuration Debug
#
# 本文件故意只用 ASCII 输出,避免 PowerShell 5.1 读无 BOM 的 UTF-8 时把中文弄乱。

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$Test,

    [switch]$NoShutdown
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'TipCL.Mac.sln'

if (-not (Test-Path $solution)) {
    throw "Solution not found: $solution"
}

Push-Location $repoRoot
try {
    if (-not $NoShutdown) {
        Write-Host '[build] dotnet build-server shutdown'
        & dotnet build-server shutdown
        if ($LASTEXITCODE -ne 0) {
            # 没有正在运行的 server 时拿到非 0 是正常现象(本机 dotnet 8/10 都这样),
            # 这一步只为防文件锁,不该让整个构建失败。见 DECISIONS.md D-06。
            Write-Warning "dotnet build-server shutdown returned $LASTEXITCODE (nothing to shut down); continuing."
        }
    }

    $buildArgs = @(
        'build', 'TipCL.Mac.sln',
        '-c', $Configuration,
        '-p:UseSharedCompilation=false',
        '-m:1',
        '-v:q',
        '-clp:ErrorsOnly;Summary'
    )

    Write-Host ('[build] dotnet ' + ($buildArgs -join ' '))
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "build failed with exit code $LASTEXITCODE"
    }

    if ($Test) {
        $testArgs = @('test', 'TipCL.Mac.sln', '-c', $Configuration, '--no-build')
        Write-Host ('[test] dotnet ' + ($testArgs -join ' '))
        & dotnet @testArgs
        if ($LASTEXITCODE -ne 0) {
            throw "tests failed with exit code $LASTEXITCODE"
        }
    }

    Write-Host '[build] OK'
}
finally {
    Pop-Location
}
