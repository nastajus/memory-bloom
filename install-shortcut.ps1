$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'release\MemoryBloom.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "MemoryBloom.exe was not found. Run build.ps1 first."
}

$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$shortcutPath = Join-Path $desktop 'Memory Bloom.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $PSScriptRoot
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = 'Memory Bloom — quiet memory-pressure monitor'
$shortcut.Save()

Write-Host "Created desktop shortcut: $shortcutPath"
