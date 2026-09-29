[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $KeyStorePath,

    [Parameter(Mandatory = $true)]
    [string] $KeyAlias,

    [Parameter(Mandatory = $true)]
    [string] $PasswordFile
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'CarScanner.csproj'
$outputPath = Join-Path $PSScriptRoot 'output\android'

$resolvedKeyStore =
    (Resolve-Path -LiteralPath $KeyStorePath).Path

$resolvedPasswordFile =
    (Resolve-Path -LiteralPath $PasswordFile).Path

if ([string]::IsNullOrWhiteSpace($KeyAlias)) {
    throw 'KeyAlias is required.'
}

New-Item -ItemType Directory -Force -Path $outputPath |
    Out-Null

dotnet publish $projectPath `
    -f net10.0-android `
    -c Release `
    --no-restore `
    -p:AndroidKeyStore=true `
    "-p:AndroidSigningKeyStore=$resolvedKeyStore" `
    "-p:AndroidSigningKeyAlias=$KeyAlias" `
    "-p:AndroidSigningKeyPass=file:$resolvedPasswordFile" `
    "-p:AndroidSigningStorePass=file:$resolvedPasswordFile" `
    -p:AndroidPackageFormats=aab `
    -p:PublishTrimmed=true `
    -o $outputPath

if ($LASTEXITCODE -ne 0) {
    throw "Android publish failed with exit code $LASTEXITCODE."
}

$bundle = Get-ChildItem -LiteralPath $outputPath -Filter '*-Signed.aab' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $bundle) {
    throw 'Publish completed, but a signed AAB was not found.'
}

Write-Host "Signed bundle: $($bundle.FullName)"
