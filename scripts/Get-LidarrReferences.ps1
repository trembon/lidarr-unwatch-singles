param(
    [string]$Version = '3.1.6.5078',
    [ValidateSet('linux-x64', 'win-x64')]
    [string]$RuntimeIdentifier = $(if ($env:OS -eq 'Windows_NT') { 'win-x64' } else { 'linux-x64' })
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Lidarr version must contain four numeric components; received '$Version'."
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repositoryRoot "artifacts/lidarr/$Version/$RuntimeIdentifier"
$distributionDirectory = Join-Path $artifactRoot 'distribution'
$referencesDirectory = Join-Path $artifactRoot 'references'
$archivePath = Join-Path $artifactRoot 'lidarr-distribution.archive'
$assetName = if ($RuntimeIdentifier -eq 'win-x64') {
    "Lidarr.develop.$Version.windows-core-x64.zip"
}
else {
    "Lidarr.develop.$Version.linux-core-x64.tar.gz"
}
$applicationDirectory = Join-Path $distributionDirectory 'Lidarr'
$applicationDirectoryExists =
    (Test-Path -LiteralPath (Join-Path $distributionDirectory 'Lidarr.Core.dll')) -or
    (Test-Path -LiteralPath (Join-Path $applicationDirectory 'Lidarr.Core.dll'))

if ((Test-Path -LiteralPath (Join-Path $referencesDirectory 'Lidarr.Core.dll')) -and
    $applicationDirectoryExists) {
    Write-Output $referencesDirectory
    exit 0
}

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

$headers = @{
    'Accept' = 'application/vnd.github+json'
    'User-Agent' = 'lidarr-unwatch-singles-build'
    'X-GitHub-Api-Version' = '2022-11-28'
}
$releaseUri = "https://api.github.com/repos/Lidarr/Lidarr/releases/tags/v$Version"
$release = Invoke-RestMethod -Uri $releaseUri -Headers $headers
$asset = $release.assets | Where-Object { $_.name -eq $assetName } | Select-Object -First 1

if ($null -eq $asset -or $asset.digest -notmatch '^sha256:[a-fA-F0-9]{64}$') {
    throw "The official Lidarr release does not provide '$assetName' with a SHA-256 digest."
}

$distributionExists =
    (Test-Path -LiteralPath (Join-Path $distributionDirectory 'Lidarr.Core.dll')) -or
    (Test-Path -LiteralPath (Join-Path $distributionDirectory 'Lidarr/Lidarr.Core.dll'))
$expectedHash = $asset.digest.Substring('sha256:'.Length).ToUpperInvariant()
$archiveExists = Test-Path -LiteralPath $archivePath

if ($distributionExists -and $archiveExists) {
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedHash) {
        Remove-Item -LiteralPath $archivePath -Force
        $archiveExists = $false
    }
}

if (!$archiveExists) {
    Invoke-WebRequest -Uri $asset.browser_download_url -Headers @{ 'User-Agent' = 'lidarr-unwatch-singles-build' } -OutFile $archivePath
}

$actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
if ($actualHash -ne $expectedHash) {
    Remove-Item -LiteralPath $archivePath -Force
    throw "Lidarr distribution checksum failed. Expected $expectedHash, received $actualHash."
}

if (!$distributionExists) {
    if (Test-Path -LiteralPath $distributionDirectory) {
        Remove-Item -LiteralPath $distributionDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $distributionDirectory -Force | Out-Null

    if ($RuntimeIdentifier -eq 'win-x64') {
        Expand-Archive -LiteralPath $archivePath -DestinationPath $distributionDirectory -Force
    }
    else {
        & tar -xzf $archivePath -C $distributionDirectory
        if ($LASTEXITCODE -ne 0) {
            throw "Extracting the verified Lidarr distribution failed with exit code $LASTEXITCODE."
        }
    }
}

$applicationDirectory = Join-Path $distributionDirectory 'Lidarr'
if (!(Test-Path -LiteralPath (Join-Path $applicationDirectory 'Lidarr.Core.dll'))) {
    $applicationDirectory = $distributionDirectory
}

$coreAssembly = Join-Path $applicationDirectory 'Lidarr.Core.dll'
$commonAssembly = Join-Path $applicationDirectory 'Lidarr.Common.dll'
if (!(Test-Path -LiteralPath $coreAssembly) -or !(Test-Path -LiteralPath $commonAssembly)) {
    throw "The verified Lidarr distribution does not contain Lidarr.Core.dll and Lidarr.Common.dll at its root."
}

New-Item -ItemType Directory -Path $referencesDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $applicationDirectory -Filter '*.dll' -File |
    Copy-Item -Destination $referencesDirectory -Force

Write-Information "Verified Lidarr $Version ($RuntimeIdentifier); SHA-256 $actualHash." -InformationAction Continue
Write-Output $referencesDirectory
