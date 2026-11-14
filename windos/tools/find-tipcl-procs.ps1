Get-Process -Id 15344 -ErrorAction SilentlyContinue | Select-Object Id,ProcessName,Path | Format-List
Get-Process -Name "TipCL" -ErrorAction SilentlyContinue | Select-Object Id,ProcessName,Path | Format-List
"done"
