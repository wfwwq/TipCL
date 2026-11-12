# TipCL.mac 打包脚本(B3 建立)
#
# 用法(工作目录随意,脚本自己切到仓库根):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\package.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\package.ps1 -NuGetSource https://api.nuget.org/v3/index.json
#
# 步骤(任一步非 0 立即停手,并把该步的退出码原样返回):
#   1) dotnet build-server shutdown        —— 防文件锁的预备动作,拿不到 0 只 warning(见 DECISIONS.md D-06)
#   2) dotnet publish src\TipCL.App -r osx-arm64 --self-contained true -o artifacts\publish\osx-arm64
#   3) 同上,-r osx-x64
#   4) dotnet run --project tools\TipCL.Packager --
#        --arm64 <arm64 发布目录> --x64 <x64 发布目录> --icon packaging\TipCL.ico
#        --stage artifacts\package --out dist\TipCL-macos-universal.tar.gz
#      打包器负责组装通用 TipCL.app、写 tar.gz,并把刚写的归档从磁盘读回来做结构校验,
#      任何一项必判校验失败它自己退出非 0。
#
# 输出约定:每步一行 ASCII 状态行,加上打包器的校验清单;每步的完整输出写进
# artifacts\package-logs\*.log(不往控制台倒几十万行构建日志)。
# 本文件的字符串字面量全是 ASCII(服务器代码页可能不是 UTF-8,见 DECISIONS.md D-08);
# 文件本身存成 **UTF-8 with BOM**,好让 Windows PowerShell 5.1 正确读上面的中文注释(见 D-12)。
#
# -NuGetSource <url>:给两条 publish 命令加 --source <url>(本机 huaweicloud 镜像超时,
#   误时传 https://api.nuget.org/v3/index.json)。不传就沿用机器上的 NuGet 配置,脚本不改配置、不建 NuGet.config。

[CmdletBinding()]
param(
    [string]$NuGetSource = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'TipCL.Mac.sln'
$logDir = Join-Path $repoRoot 'artifacts\package-logs'
$archive = Join-Path $repoRoot 'dist\TipCL-macos-universal.tar.gz'

if (-not (Test-Path -LiteralPath $solution)) {
    throw "Solution not found: $solution"
}

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# 跑一条 dotnet 命令:完整输出进日志,只回一个退出码。
# 注意 2>&1:把原生命令的 stderr 并进管道后,ErrorActionPreference=Stop 会把它当终止性错误,
# 所以这一步临时切回 Continue,免得“有 warning 的构建”被误判成失败。
function Invoke-DotnetStep {
    param(
        [string]$Step,
        [string]$LogName,
        [string[]]$Arguments
    )

    $log = Join-Path $logDir $LogName
    Write-Host ('[package] ' + $Step)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & dotnet @Arguments 2>&1 | Tee-Object -FilePath $log | Out-Null
    }
    finally {
        $ErrorActionPreference = $previous
    }

    $code = $LASTEXITCODE
    if ($code -eq 0) {
        Write-Host ('[package] ' + $Step + ': OK (log ' + $LogName + ')')
    }
    else {
        Write-Host ('[package] ' + $Step + ': FAILED with exit code ' + $code + ' (log ' + $LogName + ')')
    }

    return $code
}

Push-Location $repoRoot
try {
    # 1) 防锁(非 0 只警告,见 D-06)
    $code = Invoke-DotnetStep -Step 'dotnet build-server shutdown' -LogName 'shutdown.log' `
        -Arguments @('build-server', 'shutdown')
    if ($code -ne 0) {
        Write-Warning "dotnet build-server shutdown returned $code (nothing to shut down); continuing."
    }

    # 2)+3) 两条 self-contained publish
    foreach ($rid in @('osx-arm64', 'osx-x64')) {
        $publishDir = 'artifacts\publish\' + $rid
        $arguments = @(
            'publish', 'src\TipCL.App\TipCL.App.csproj',
            '-c', 'Release',
            '-r', $rid,
            '--self-contained', 'true',
            '-p:UseSharedCompilation=false',
            '-o', $publishDir
        )
        if ($NuGetSource) {
            $arguments += @('--source', $NuGetSource)
        }

        $code = Invoke-DotnetStep -Step ('dotnet publish ' + $rid) -LogName ('publish-' + $rid + '.log') `
            -Arguments $arguments
        if ($code -ne 0) {
            Write-Host ('[package] stopped at publish ' + $rid + ' (exit ' + $code + '); see artifacts\package-logs\publish-' + $rid + '.log')
            exit $code
        }

        $count = (Get-ChildItem -LiteralPath (Join-Path $repoRoot $publishDir) -Recurse -File | Measure-Object).Count
        Write-Host ('[package] publish ' + $rid + ': ' + $count + ' files in ' + $publishDir)
    }

    # 4) 组装 .app + 写 tar.gz + 自校验
    $packArguments = @(
        'run', '--project', 'tools\TipCL.Packager',
        '-c', 'Release',
        '--no-launch-profile',
        '-p:UseSharedCompilation=false',
        '--',
        '--arm64', 'artifacts\publish\osx-arm64',
        '--x64', 'artifacts\publish\osx-x64',
        '--icon', 'packaging\TipCL.ico',
        '--stage', 'artifacts\package',
        '--out', 'dist\TipCL-macos-universal.tar.gz'
    )

    $code = Invoke-DotnetStep -Step 'packager (assemble TipCL.app + tar.gz + verify)' -LogName 'packager.log' `
        -Arguments $packArguments

    # 打包器的校验清单(OK / FAIL 逐行 + 代码签名报告)照原样转出来
    $packLog = Join-Path $logDir 'packager.log'
    if (Test-Path -LiteralPath $packLog) {
        Get-Content -LiteralPath $packLog | Where-Object {
            $_ -match '^\[packager\]|^  (OK|FAIL|\*|signed:|unsigned:)'
        } | ForEach-Object { Write-Host ('  ' + $_) }
    }

    if ($code -ne 0) {
        Write-Host ('[package] stopped at the packager (exit ' + $code + '); see artifacts\package-logs\packager.log')
        exit $code
    }

    if (Test-Path -LiteralPath $archive) {
        $size = (Get-Item -LiteralPath $archive).Length
        Write-Host ('[package] archive: dist\TipCL-macos-universal.tar.gz, ' + $size + ' bytes')
    }

    Write-Host '[package] OK'
    exit 0
}
finally {
    Pop-Location
}
