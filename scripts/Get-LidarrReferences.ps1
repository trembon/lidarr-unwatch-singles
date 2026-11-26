$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
. "$PSScriptRoot/LidarrHost.ps1"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$referencesDirectory = Join-Path $repositoryRoot "artifacts/lidarr/$lidarrPackageVersion/references"
New-Item -ItemType Directory -Path $referencesDirectory -Force | Out-Null

# A stopped container gives us the exact host assemblies without running Lidarr.
$containerId = docker create --pull missing $lidarrImage
try {
    $assemblies = @('Lidarr.Core.dll', 'Lidarr.Common.dll', 'FluentValidation.dll', 'NLog.dll')
    foreach ($assembly in $assemblies) {
        docker cp "${containerId}:/app/lidarr/bin/$assembly" (Join-Path $referencesDirectory $assembly)
    }

    $coreAssembly = Join-Path $referencesDirectory 'Lidarr.Core.dll'
    $assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($coreAssembly).Version
    if ($assemblyVersion -ne [version]$lidarrVersion) {
        throw "Expected Lidarr.Core $lidarrVersion from the pinned image, found $assemblyVersion."
    }
}
finally {
    docker rm --volumes $containerId | Out-Null
}

Write-Output $referencesDirectory
