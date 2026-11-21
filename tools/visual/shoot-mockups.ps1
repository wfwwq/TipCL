param(
  [string]$VisualDir = '',
  [string]$Only = ''
)
# Shoot every v3 mockup state with headless Edge (ASCII only).
# 1) copy <_visual>/mockups/<file>.html to <_visual>/mockups/states/<file>.html and append mockup-states.js
# 2) per state: --dump-dom to read the window rect, --screenshot of the page, crop to the window element
# Edge: --headless=new, private --user-data-dir, Start-Process -WindowStyle Hidden (no window, user's Edge untouched).
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ($VisualDir -eq '') { $VisualDir = Join-Path (Split-Path -Parent $repo) '_visual' }
$mk = Join-Path $VisualDir 'mockups'
$st = Join-Path $mk 'states'
$raw = Join-Path $st 'raw'
New-Item -ItemType Directory -Force -Path $st, $raw | Out-Null
$log = Join-Path $VisualDir 'mockups-shoot.log'
"== START $(Get-Date -Format HH:mm:ss)" | Out-File $log -Encoding utf8
$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
if (-not (Test-Path $edge)) { $edge = 'C:\Program Files\Microsoft\Edge\Application\msedge.exe' }
$prof = Join-Path $VisualDir 'edge-profile'
$py = 'python'

$inject = Get-Content (Join-Path $PSScriptRoot 'mockup-states.js') -Raw -Encoding UTF8
$cfg = Get-Content (Join-Path $PSScriptRoot 'mockup-states.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$utf8 = New-Object System.Text.UTF8Encoding($false)
foreach ($f in ($cfg.states | ForEach-Object { $_.file } | Sort-Object -Unique)) {
  $src = Join-Path $mk $f
  $html = [System.IO.File]::ReadAllText($src, $utf8)
  $idx = $html.LastIndexOf('</body>')
  $tag = "<script>/* AI3-visual state injector */`n" + $inject + "`n</script>`n"
  if ($idx -ge 0) { $html = $html.Substring(0, $idx) + $tag + $html.Substring($idx) } else { $html = $html + $tag }
  [System.IO.File]::WriteAllText((Join-Path $st $f), $html, $utf8)
  "injected $f" | Out-File $log -Append -Encoding utf8
}

function Run-Edge([string[]]$EdgeArgs, [string]$StdOut) {
  $common = @('--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run', '--no-default-browser-check',
              '--force-device-scale-factor=1', '--virtual-time-budget=4000', "--user-data-dir=`"$prof`"")
  $all = $common + $EdgeArgs
  if ($StdOut -ne '') {
    $p = Start-Process -FilePath $edge -WindowStyle Hidden -PassThru -Wait -ArgumentList $all -RedirectStandardOutput $StdOut -RedirectStandardError ($StdOut + '.err')
  } else {
    $p = Start-Process -FilePath $edge -WindowStyle Hidden -PassThru -Wait -ArgumentList $all
  }
  return $p.ExitCode
}

foreach ($s in $cfg.states) {
  if ($Only -ne '' -and $s.name -notlike $Only) { continue }
  $page = Join-Path $st $s.file
  $url = ([System.Uri]$page).AbsoluteUri + '#' + $s.hash
  $dom = Join-Path $raw ($s.name + '.dom.html')
  $c1 = Run-Edge @('--window-size=1500,2400', '--dump-dom', "`"$url`"") $dom
  $txt = ''
  if (Test-Path $dom) { $txt = [System.IO.File]::ReadAllText($dom, $utf8) }
  $m = [regex]::Match($txt, '<pre id="__tour_rect"[^>]*>(.*?)</pre>')
  if (-not $m.Success) { "$($s.name): no rect (dump exit=$c1)" | Out-File $log -Append -Encoding utf8; continue }
  $rect = [System.Net.WebUtility]::HtmlDecode($m.Groups[1].Value) | ConvertFrom-Json
  if (-not $rect.ok) { "$($s.name): element not found $($rect.hash)" | Out-File $log -Append -Encoding utf8; continue }
  $hgt = [int]($rect.y + $rect.h + 40)
  if ($hgt -lt 600) { $hgt = 600 }
  $full = Join-Path $raw ($s.name + '.full.png')
  Remove-Item $full -ErrorAction SilentlyContinue
  $c2 = Run-Edge @("--window-size=1500,$hgt", "--screenshot=`"$full`"", "`"$url`"") ''
  $dst = Join-Path $st ($s.name + '.png')
  & $py (Join-Path $PSScriptRoot 'crop.py') $full $rect.x $rect.y $rect.w $rect.h $dst 2>&1 | Out-File $log -Append -Encoding utf8
  "$($s.name): rect=$($rect.x),$($rect.y) $($rect.w)x$($rect.h) done=$($rect.done -join ',') shot=$c2 png=$(Test-Path $dst)" | Out-File $log -Append -Encoding utf8
}
"== END $(Get-Date -Format HH:mm:ss)" | Out-File $log -Append -Encoding utf8
