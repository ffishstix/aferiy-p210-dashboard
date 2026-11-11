param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'BatteryWatch.csproj'
$output = Join-Path (Split-Path -Parent $PSScriptRoot) 'cloud\public\downloads'
foreach ($target in @('win-x64', 'win-arm64', 'win-x86')) {
    $destination = Join-Path $output $target
    & $Dotnet publish $project -c Release -r $target --self-contained false `
        -p:PublishSingleFile=true -p:DebugType=None -o $destination
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $target" }
    Get-FileHash -LiteralPath (Join-Path $destination 'BatteryWatch.exe') -Algorithm SHA256 |
        ForEach-Object { Write-Output "$target $($_.Hash)" }
}
