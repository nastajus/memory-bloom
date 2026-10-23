param([switch]$LaunchNow)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'release\MemoryBloom.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "MemoryBloom.exe was not found. Run build.ps1 first."
}
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Set-ItemProperty -Path $runKey -Name 'Memory Bloom' -Value ('"{0}"' -f $exe)
Write-Host "Memory Bloom will now start when you sign in."
& (Join-Path $PSScriptRoot 'install-shortcut.ps1')
if ($LaunchNow) { Start-Process -FilePath $exe }
