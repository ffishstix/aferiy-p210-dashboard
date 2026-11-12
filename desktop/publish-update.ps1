param(
    [string]$Dotnet = 'dotnet',
    [string]$PrivateKey = (Join-Path $env:LOCALAPPDATA 'AferiyMonitor\release-private.pem'),
    [string]$CodeSignThumbprint
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $PrivateKey)) { throw 'Release signing key is missing' }
$certificates = Get-ChildItem Cert:\CurrentUser\My | Where-Object {
    $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) -and
    ($_.EnhancedKeyUsageList | Where-Object ObjectId -eq '1.3.6.1.5.5.7.3.3')
}
if ($CodeSignThumbprint) {
    $certificate = $certificates | Where-Object Thumbprint -eq $CodeSignThumbprint | Select-Object -First 1
} else {
    $certificate = $certificates | Where-Object Subject -eq 'CN=Fishstix Battery Watch' | Sort-Object NotAfter -Descending | Select-Object -First 1
}
if (-not $certificate) { throw 'A code-signing certificate with its private key is missing' }
$signTool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction Stop |
    Where-Object { $_.DirectoryName -match '\\x64$' } | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $signTool) { throw 'Windows SignTool is missing' }
& (Join-Path $PSScriptRoot 'build.ps1') -Dotnet $Dotnet
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed' }
$root = Split-Path -Parent $PSScriptRoot
foreach ($architecture in @('win-x64', 'win-arm64', 'win-x86')) {
    $exe = Join-Path $root "cloud\public\downloads\$architecture\BatteryWatch.exe"
    & $signTool sign /sha1 $certificate.Thumbprint /s My /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /d 'Battery Watch' $exe
    if ($LASTEXITCODE -ne 0) { throw "Windows code signing failed for $architecture" }
    $signature = Get-AuthenticodeSignature -LiteralPath $exe
    if ($signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint -or -not $signature.TimeStamperCertificate) {
        throw "Windows signature or timestamp is missing for $architecture"
    }
}
& $Dotnet run --project (Join-Path $PSScriptRoot 'ReleaseTool\ReleaseTool.csproj') -c Release -- $PrivateKey $root
if ($LASTEXITCODE -ne 0) { throw 'Release signing failed' }
