Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Screen]::AllScreens | ForEach-Object { $_.Bounds.ToString() }
"WorkArea: $([System.Windows.Forms.SystemInformation]::WorkingArea)"
