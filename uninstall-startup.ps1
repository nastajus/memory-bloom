$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $runKey -Name 'Memory Bloom' -ErrorAction SilentlyContinue
Write-Host "Memory Bloom was removed from sign-in startup."
