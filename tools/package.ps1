# tools/package.ps1 — TipCL Linux 版一键双端打包（M3-C2，Windows 上跑）
# 用法:
#   powershell -ExecutionPolicy Bypass -File tools/package.ps1                # 双端发布到 dist/
#   powershell -ExecutionPolicy Bypass -File tools/package.ps1 -SkipLinux     # 只出 win-x64
#   powershell -ExecutionPolicy Bypass -File tools/package.ps1 -SkipWin       # 只出 linux-x64
#
# 产物:
#   dist/TipCL-linux-x64/TipCL.App      Linux x64 self-contained 单文件（chmod +x 后直接跑）
#   dist/TipCL-win-x64/TipCL.App.exe    Windows x64 self-contained 单文件
#   dist/TipCL-linux-x64.zip / dist/TipCL-win-x64.zip  对应压缩包
#
# 发布参数（与 tools/package.sh 同源，实测记录见 progress/M3-C2.md）:
#   --self-contained            不依赖目标机 .NET 运行时
#   -p:PublishSingleFile=true   单文件宿主
#   -p:IncludeNativeLibrariesForSelfExtract=true   Skia/HarfBuzz 原生库并入单文件
#   -p:EnableCompressionInSingleFile=true          压缩（72MB -> ~43MB）
#   不开 PublishTrimmed：Avalonia 11 XAML/样式反射 + Core 反编译反射代码，trim 兼容性
#   无法在本机（Windows）验证 Linux 运行时行为，M3 决策默认不 trim（取舍记录见 progress/M3-C2.md）。
param(
    [switch]$SkipLinux,
    [switch]$SkipWin
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$publishArgs = @(
    "-c", "Release", "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:UseSharedCompilation=false", "-m:1"
)

function Invoke-Publish([string]$Rid, [string]$OutDir) {
    Write-Host "`n===== dotnet publish ($Rid) =====" -ForegroundColor Cyan
    dotnet build-server shutdown | Out-Null
    dotnet publish src/TipCL.App @publishArgs -r $Rid -o $OutDir
    if ($LASTEXITCODE -ne 0) { Write-Error "publish $Rid 失败（退出码 $LASTEXITCODE）。"; exit $LASTEXITCODE }
    # 只留单文件本体；pdb 调试符号不随包分发
    Get-ChildItem $OutDir -Exclude *.exe, "TipCL.App" | Remove-Item -Recurse -Force
    $bin = Get-ChildItem $OutDir | Select-Object -First 1
    Write-Host ("产物: {0} ({1:N1} MB)" -f $bin.FullName, ($bin.Length / 1MB))
}

function Compress-Product([string]$Dir, [string]$Zip) {
    if (Test-Path $Zip) { Remove-Item $Zip -Force }
    Compress-Archive -Path (Join-Path $Dir "*") -DestinationPath $Zip
    Write-Host ("压缩包: {0} ({1:N1} MB)" -f $Zip, ((Get-Item $Zip).Length / 1MB))
}

if (-not $SkipLinux) {
    Invoke-Publish "linux-x64" "dist/TipCL-linux-x64"
    Compress-Product "dist/TipCL-linux-x64" "dist/TipCL-linux-x64.zip"
}

if (-not $SkipWin) {
    Invoke-Publish "win-x64" "dist/TipCL-win-x64"
    Compress-Product "dist/TipCL-win-x64" "dist/TipCL-win-x64.zip"

    # Windows 产物冒烟：--smoke 应退出码 0 并产出 smoke.png（Linux 产物无法在本机运行）
    Write-Host "`n===== win-x64 冒烟 (--smoke) =====" -ForegroundColor Cyan
    if (Test-Path "smoke.png") { Remove-Item "smoke.png" -Force }
    & "dist/TipCL-win-x64/TipCL.App.exe" --smoke
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path "smoke.png") -or (Get-Item "smoke.png").Length -lt 1KB) {
        Write-Error "win-x64 单文件冒烟失败。"; exit 1
    }
    Write-Host "win-x64 冒烟通过: smoke.png ($((Get-Item 'smoke.png').Length) B)"
}

Write-Host "`n打包完成。" -ForegroundColor Green
Write-Host "Linux 产物使用: chmod +x dist/TipCL-linux-x64/TipCL.App && ./dist/TipCL-linux-x64/TipCL.App"
Write-Host "AppImage 组装见 tools/package.sh（需在 Linux 上跑）。"
Write-Host "  AppImage 组装也可以拆开做: 本机发布 dist/TipCL-linux-x64/TipCL.App, 拷到 Linux 上跑 tools/package.sh --appimage-only"
Write-Host "  前置条件(见脚本注释): file 包必须装; 没有 /dev/fuse 时给 linuxdeploy 与产物前缀 APPIMAGE_EXTRACT_AND_RUN=1"
