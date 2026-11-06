param([string]$Exe, [string]$WorkDir)
$ErrorActionPreference = "Stop"
Add-Type -Namespace Win32 -Name PclMeasure -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
[DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT r);
[DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll")] public static extern int GetWindowTextW(IntPtr hWnd, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder text, int count);
public struct RECT { public int Left, Top, Right, Bottom; }
'@

$proc = Start-Process -FilePath $Exe -WorkingDirectory $WorkDir -PassThru
$deadline = (Get-Date).AddSeconds(90)
$best = [IntPtr]::Zero; $bestArea = 0; $bestTitle = ""
while ((Get-Date) -lt $deadline) {
  Start-Sleep -Milliseconds 700
  if ($proc.HasExited) { break }
  $proc.Refresh()
  $proc.WaitForInputIdle(300) | Out-Null
  $procs = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
  if (-not $procs) { break }
  foreach ($w in (Get-Process | Where-Object { $_.Id -eq $proc.Id })) {
    $h = $w.MainWindowHandle
  }
  $sb = New-Object System.Text.StringBuilder 256
  $found = [IntPtr]::Zero; $foundArea = 0; $foundTitle = ""
  $typeAdd = {
    param($h)
    if ([Win32.PclMeasure]::IsWindowVisible($h)) {
      $r = New-Object Win32.PclMeasure+RECT
      [Win32.PclMeasure]::GetWindowRect($h, [ref]$r) | Out-Null
      $area = ($r.Right - $r.Left) * ($r.Bottom - $r.Top)
      if ($area -gt $foundArea) {
        $sb.Clear() | Out-Null
        [Win32.PclMeasure]::GetWindowTextW($h, $sb, 256) | Out-Null
        if ($sb.ToString() -notmatch "CiceroUIWndFrame|MSCTFIME UI|Default IME") {
          $found = $h; $foundArea = $area; $foundTitle = $sb.ToString()
        }
      }
    }
  }
  $src = @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class WinEnum {
  public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  public static List<IntPtr> ForProcess(int pid) {
    var list = new List<IntPtr>();
    return list;
  }
}
"@
  if (-not ("WinEnum" -as [type])) { Add-Type -TypeDefinition $src }
  foreach ($h in [WinEnum]::ForProcess($proc.Id)) { & $typeAdd $h }
  if ($found -ne [IntPtr]::Zero -and $foundArea -gt 40000) { $best = $found; $bestArea = $foundArea; $bestTitle = $foundTitle; break }
}
if ($best -eq [IntPtr]::Zero) { "NO WINDOW (exited=$($proc.HasExited))"; if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }; exit 2 }
Start-Sleep -Seconds 6

$wr = New-Object Win32.PclMeasure+RECT
$cr = New-Object Win32.PclMeasure+RECT
[Win32.PclMeasure]::GetWindowRect($best, [ref]$wr) | Out-Null
[Win32.PclMeasure]::GetClientRect($best, [ref]$cr) | Out-Null
$dpi = [Win32.PclMeasure]::GetDpiForWindow($best)
$scale = $dpi / 96.0

"WindowTitle: $bestTitle"
"DPI: $dpi (scale=$scale)"
$ow = $wr.Right - $wr.Left; $oh = $wr.Bottom - $wr.Top
"WindowFrame(physical px): ${ow}x${oh}"
"ClientRect(physical px):  $($cr.Right - $cr.Left)x$($cr.Bottom - $cr.Top)"
"WindowFrame(DIU @96dpi):  $([math]::Round($ow / $scale))x$([math]::Round($oh / $scale))"
"ClientRect(DIU @96dpi):   $([math]::Round(($cr.Right - $cr.Left) / $scale))x$([math]::Round(($cr.Bottom - $cr.Top) / $scale))"

if (-not $proc.HasExited) {
  $null = $proc.CloseMainWindow()
  Start-Sleep -Seconds 3
  $proc.Refresh()
  if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
}
"Closed: $($proc.HasExited)"
