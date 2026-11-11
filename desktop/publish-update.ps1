param(
    [string]$Dotnet = 'dotnet',
    [string]$PrivateKey = (Join-Path $env:LOCALAPPDATA 'AferiyMonitor\release-private.pem')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $PrivateKey)) { throw 'Release signing key is missing' }
& (Join-Path $PSScriptRoot 'build.ps1') -Dotnet $Dotnet
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed' }
$root = Split-Path -Parent $PSScriptRoot
& $Dotnet run --project (Join-Path $PSScriptRoot 'ReleaseTool\ReleaseTool.csproj') -c Release -- $PrivateKey $root
if ($LASTEXITCODE -ne 0) { throw 'Release signing failed' }
