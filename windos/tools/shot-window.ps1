param([string]$Exe, [string]$Out, [int]$WaitSec = 9, [int]$Keep = 0)
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public struct RECT { public int L, T, R, B; }
public class W2 {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
[W2]::SetProcessDPIAware() | Out-Null
$p = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds $WaitSec
$p.Refresh(); $h = $p.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { Write-Output "NO WINDOW"; Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue; exit 1 }
[W2]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 600
$r = New-Object RECT; [W2]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $ht = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap($w, $ht)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
if ($Keep -eq 0) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
Write-Output "saved: $Out"
