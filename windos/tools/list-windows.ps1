param([string]$Exe, [string]$WorkDir, [int]$ProbeSeconds = 40)
$src = @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WinEnum2 {
  public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern int GetWindowTextW(IntPtr hWnd, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder t, int c);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
  public struct RECT { public int Left, Top, Right, Bottom; }
  public static List<string> All() {
    var rows = new List<string>();
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (IsWindowVisible(h)) {
        var sb = new StringBuilder(256);
        GetWindowTextW(h, sb, 256);
        RECT r; GetWindowRect(h, out r);
        string title = sb.ToString();
        if (title.Length > 0 && (r.Right - r.Left) > 100 && (r.Bottom - r.Top) > 80)
          rows.Add(p + "|" + title + "|" + (r.Right - r.Left) + "x" + (r.Bottom - r.Top));
      }
      return true;
    }, IntPtr.Zero);
    return rows;
  }
}
"@
if (-not ("WinEnum2" -as [type])) { Add-Type -TypeDefinition $src }
$proc = Start-Process -FilePath $Exe -WorkingDirectory $WorkDir -PassThru
$seen = @{}
for ($i = 0; $i -lt $ProbeSeconds; $i += 2) {
  Start-Sleep -Seconds 2
  foreach ($row in [WinEnum2]::All()) {
    $pid2 = ($row -split '\|')[0]
    if (-not $seen.ContainsKey($row)) { $seen[$row] = 1; Write-Host "t=${i}s $row" }
  }
  if ($proc.HasExited) { Write-Host "launcher exited"; break }
  foreach ($c in (Get-CimInstance Win32_Process -Filter "ParentProcessId=$($proc.Id)" -ErrorAction SilentlyContinue)) {
    Write-Host "child: $($c.ProcessId) $($c.Name)"
  }
}
Get-Process -Name "Plain Craft Launcher 2" -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "alive: $($_.Id) $($_.MainWindowTitle)" }
if (-not $proc.HasExited) { $null = $proc.CloseMainWindow(); Start-Sleep 2; if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force } }
Get-Process | Where-Object { $_.ProcessName -like "*Plain*Craft*" } | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
"probe done"
