$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot 'release'
dotnet publish $PSScriptRoot -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
Write-Host "Built: $output\MemoryBloom.exe"
