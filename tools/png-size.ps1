param([string]$PngPath)
Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile($PngPath)
"$($img.Width)x$($img.Height)"
$img.Dispose()
