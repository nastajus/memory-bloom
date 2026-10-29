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
        try {
            if ($_.Threads.Count -eq 0) { return $false }
            $processPath = $_.Path
            [string]::IsNullOrWhiteSpace($processPath) -or $processPath -eq $resolvedExecutable
        }
        catch {
            # Some Windows security contexts expose the process by name but redact
            # its executable path. MemoryBloom is project-specific, so retain it as
            # a safe restart candidate instead of leaving the release binary locked.
            $true
        }
    }
foreach ($item in $running) {
    Stop-Process -Id $item.Id -Force
    [void]$item.WaitForExit(3000)
}

if ($ReplacementExecutable) {
    $replacement = (Resolve-Path -LiteralPath $ReplacementExecutable).Path
    try {
        Copy-Item -LiteralPath $replacement -Destination $resolvedExecutable -Force
    }
    catch [System.IO.IOException] {
        # Windows can briefly retain a zero-thread process object that keeps the
        # old image mapped. Renaming that file preserves it while freeing the
        # canonical path for the verified replacement.
        $retiredName = 'MemoryBloom.retired-{0:yyyyMMdd-HHmmss}.exe' -f (Get-Date)
        $retiredPath = Join-Path (Split-Path -Parent $resolvedExecutable) $retiredName
        Move-Item -LiteralPath $resolvedExecutable -Destination $retiredPath
        Copy-Item -LiteralPath $replacement -Destination $resolvedExecutable
        Write-Host "Retained the locked previous image as $retiredName."
    }
}

$arguments = if ($Show) { '--show-passive' } else { $null }
$process = Start-Process -FilePath $resolvedExecutable -ArgumentList $arguments -PassThru
Start-Sleep -Milliseconds 450

$focusRestored = $false
if ($previousWindow -ne [IntPtr]::Zero -and [MemoryBloom.ForegroundWindow]::IsWindow($previousWindow)) {
    $focusRestored = [MemoryBloom.ForegroundWindow]::SetForegroundWindow($previousWindow)
}

Write-Host "Memory Bloom restarted without taking lasting focus (PID $($process.Id); prior focus restored: $focusRestored)."
