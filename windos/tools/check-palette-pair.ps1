param([string]$Dark, [string]$Light)
$dk = Select-String -Path $Dark -Pattern 'x:Key="([^"]+)"' -AllMatches | ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
$lt = Select-String -Path $Light -Pattern 'x:Key="([^"]+)"' -AllMatches | ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
$onlyDark = $dk | Where-Object { $lt -notcontains $_ }
$onlyLight = $lt | Where-Object { $dk -notcontains $_ }
"Dark keys: $($dk.Count)  Light keys: $($lt.Count)"
if ($onlyDark) { "ONLY IN DARK: $($onlyDark -join ', ')" }
if ($onlyLight) { "ONLY IN LIGHT: $($onlyLight -join ', ')" }
if (-not $onlyDark -and -not $onlyLight) { "PAIRED OK" }
