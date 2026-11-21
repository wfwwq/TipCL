param(
  [string]$Script = '',
  [string]$Out = '',
  [string]$VisualDir = '',
  [switch]$Build
)
# Visual tour runner (ASCII only: Windows PowerShell 5 reads BOM-less .ps1 as ANSI).
# Runs `TipCL.App.dll --visual-tour <script> <out>` on the Avalonia headless platform:
# no OS window is created, no browser / file manager is opened, the game is never launched.
# TIPCL_HOME is a private temp dir (the tour builds its fake-data fixture there); never the real one.
$dn = 'C:\Users\Administrator\.r35-dotnet\dotnet.exe'
if (-not (Test-Path $dn)) { $dn = 'dotnet' }
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ($VisualDir -eq '') { $VisualDir = Join-Path (Split-Path -Parent $repo) '_visual' }
if ($Script -eq '') { $Script = Join-Path $PSScriptRoot 'tours\full.json' }
if ($Out -eq '') { $Out = Join-Path $VisualDir 'tour' }
New-Item -ItemType Directory -Force -Path $VisualDir, $Out | Out-Null
$log = Join-Path $VisualDir 'tour.log'
"== START $(Get-Date -Format HH:mm:ss) script=$Script" | Out-File $log -Encoding utf8

if ($Build) {
  Set-Location $repo
  & $dn build src\TipCL.App\TipCL.App.csproj -c Debug -p:UseSharedCompilation=false -m:1 --nologo *> (Join-Path $VisualDir 'tour-build.log')
  "build exit=$LASTEXITCODE" | Out-File $log -Append -Encoding utf8
}
$dll = Join-Path $repo 'src\TipCL.App\bin\Debug\net8.0\TipCL.App.dll'

$home2 = Join-Path $VisualDir 'home-tour'
if (Test-Path $home2) { Remove-Item -Recurse -Force $home2 -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $home2 | Out-Null
Get-ChildItem $Out -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
$env:TIPCL_HOME = $home2
Set-Location $Out
& $dn $dll --visual-tour $Script $Out *> (Join-Path $VisualDir 'tour-run.log')
"tour exit=$LASTEXITCODE" | Out-File $log -Append -Encoding utf8
"== END $(Get-Date -Format HH:mm:ss)" | Out-File $log -Append -Encoding utf8
