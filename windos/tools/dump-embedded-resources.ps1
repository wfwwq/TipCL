param([string]$ExePath)
if (-not $ExePath) { throw "usage: -File dump-embedded-resources.ps1 -ExePath <path>" }
$a = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($ExePath)
$names = $a.GetManifestResourceNames() | Sort-Object
$total = 0
$rows = foreach ($n in $names) {
  $s = $a.GetManifestResourceStream($n)
  $len = $s.Length
  $total += $len
  [PSCustomObject]@{ Bytes = $len; Name = $n }
}
$rows | Sort-Object Bytes -Descending | Format-Table -AutoSize | Out-String -Width 200
"TOTAL embedded resources: $total bytes across $($rows.Count) entries"
