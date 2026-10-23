$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'release\MemoryBloom.NativeHost.exe'
if (-not (Test-Path -LiteralPath $source)) { throw "Build Memory Bloom first; native host not found at $source" }

$install = Join-Path $env:LOCALAPPDATA 'MemoryBloom\FirefoxCompanion'
New-Item -ItemType Directory -Force -Path $install | Out-Null
$hostPath = Join-Path $install 'MemoryBloom.NativeHost.exe'
Copy-Item -LiteralPath $source -Destination $hostPath -Force
$manifestPath = Join-Path $install 'memory_bloom.json'
$manifest = @{
    name = 'memory_bloom'
    description = 'Local tab metadata bridge for Memory Bloom'
    path = $hostPath
    type = 'stdio'
    allowed_extensions = @('memory-bloom@local')
} | ConvertTo-Json -Depth 3
Set-Content -LiteralPath $manifestPath -Value $manifest -Encoding utf8
$key = 'HKCU:\Software\Mozilla\NativeMessagingHosts\memory_bloom'
New-Item -Path $key -Force | Out-Null
Set-Item -Path $key -Value $manifestPath
Write-Host "Installed Firefox native host: $manifestPath"
