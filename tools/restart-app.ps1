param(
    [switch]$Show,
    [string]$ReplacementExecutable
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot 'release\MemoryBloom.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw 'MemoryBloom.exe was not found. Run build.ps1 first.'
}

if (-not ('MemoryBloom.ForegroundWindow' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
namespace MemoryBloom {
    public static class ForegroundWindow {
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetForegroundWindow(IntPtr window);
    }
}
'@
}

$previousWindow = [MemoryBloom.ForegroundWindow]::GetForegroundWindow()
$resolvedExecutable = (Resolve-Path -LiteralPath $executable).Path
$running = Get-Process -Name MemoryBloom -ErrorAction SilentlyContinue |
    Where-Object {
        try { $_.Path -eq $resolvedExecutable } catch { $false }
    }
foreach ($item in $running) {
    Stop-Process -Id $item.Id -Force
    [void]$item.WaitForExit(3000)
}

if ($ReplacementExecutable) {
    $replacement = (Resolve-Path -LiteralPath $ReplacementExecutable).Path
    Copy-Item -LiteralPath $replacement -Destination $resolvedExecutable -Force
}

$arguments = if ($Show) { '--show-passive' } else { $null }
$process = Start-Process -FilePath $resolvedExecutable -ArgumentList $arguments -PassThru
Start-Sleep -Milliseconds 450

$focusRestored = $false
if ($previousWindow -ne [IntPtr]::Zero -and [MemoryBloom.ForegroundWindow]::IsWindow($previousWindow)) {
    $focusRestored = [MemoryBloom.ForegroundWindow]::SetForegroundWindow($previousWindow)
}

Write-Host "Memory Bloom restarted without taking lasting focus (PID $($process.Id); prior focus restored: $focusRestored)."
