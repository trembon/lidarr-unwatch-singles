param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$RepositoryOwner,

    [Parameter(Mandatory = $true)]
    [string]$RepositoryName,

    [Parameter(Mandatory = $true)]
    [string]$LidarrReferencePath,

    [string]$OutputDirectory = 'artifacts/release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw "Version must be a semantic version without its leading v; received '$Version'."
}

$absoluteReferencePath = [System.IO.Path]::GetFullPath($LidarrReferencePath, $repositoryRoot)
if (!(Test-Path -LiteralPath (Join-Path $absoluteReferencePath 'Lidarr.Core.dll'))) {
    throw "The verified Lidarr reference directory '$absoluteReferencePath' is missing Lidarr.Core.dll."
}

$projectPath = Join-Path $repositoryRoot 'src/Lidarr.Plugin.UnwatchSingles/Lidarr.Plugin.UnwatchSingles.csproj'
$nuGetConfigPath = Join-Path $repositoryRoot 'NuGet.Config'
$pluginOutput = Join-Path $repositoryRoot 'src/Lidarr.Plugin.UnwatchSingles/bin/Release/net8.0'
$absoluteOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory, $repositoryRoot)
$packageName = 'Lidarr.Plugin.UnwatchSingles.net8.0.zip'
$packagePath = Join-Path $absoluteOutputDirectory $packageName
$checksumPath = "$packagePath.sha256"

dotnet restore $projectPath --configfile $nuGetConfigPath
if ($LASTEXITCODE -ne 0) {
    throw "Restoring the plugin project failed with exit code $LASTEXITCODE."
}

dotnet build $projectPath -c Release --no-restore `
    "-p:Version=$Version" `
    "-p:RepositoryOwner=$RepositoryOwner" `
    "-p:RepositoryName=$RepositoryName" `
    "-p:LidarrReferencePath=$absoluteReferencePath"
if ($LASTEXITCODE -ne 0) {
    throw "Building the plugin failed with exit code $LASTEXITCODE."
}

$pluginAssembly = Join-Path $pluginOutput 'Lidarr.Plugin.UnwatchSingles.dll'
$pluginSymbols = Join-Path $pluginOutput 'Lidarr.Plugin.UnwatchSingles.pdb'
foreach ($file in @($pluginAssembly, $pluginSymbols)) {
    if (!(Test-Path -LiteralPath $file)) {
        throw "The build is missing required release file '$file'."
    }
}

New-Item -ItemType Directory -Path $absoluteOutputDirectory -Force | Out-Null
if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}

$packageArchive = [System.IO.Compression.ZipFile]::Open(
    $packagePath,
    [System.IO.Compression.ZipArchiveMode]::Create)
try {
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $packageArchive, $pluginAssembly, 'Lidarr.Plugin.UnwatchSingles.dll')
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $packageArchive, $pluginSymbols, 'Lidarr.Plugin.UnwatchSingles.pdb')
}
finally {
    $packageArchive.Dispose()
}

$entries = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $entryNames = @($entries.Entries | Select-Object -ExpandProperty FullName | Sort-Object)
    if ($entryNames.Count -ne 2 -or
        $entryNames -notcontains 'Lidarr.Plugin.UnwatchSingles.dll' -or
        $entryNames -notcontains 'Lidarr.Plugin.UnwatchSingles.pdb') {
        throw "The plugin archive contains unexpected files: $($entryNames -join ', ')."
    }
}
finally {
    $entries.Dispose()
}

$hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$hash  $packageName" -NoNewline -Encoding ascii

Write-Output ([System.IO.Path]::GetFullPath($packagePath))
