param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [string]$RepositoryOwner,

    [Parameter(Mandatory = $true)]
    [string]$RepositoryName,

    [string]$ExpectedPluginVersion = '0.2.0',

    [int]$StartupTimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
. "$PSScriptRoot/LidarrHost.ps1"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$packagePath = [System.IO.Path]::GetFullPath($PackagePath, $repositoryRoot)

if (!(Test-Path -LiteralPath $packagePath)) {
    throw "Plugin package '$packagePath' does not exist."
}

$packageName = Split-Path -Leaf $packagePath
$hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($packageName -ne 'Lidarr.Plugin.UnwatchSingles.net10.0.zip' -or
    (Get-Content -LiteralPath "$packagePath.sha256" -Raw).TrimEnd() -cne "$hash  $packageName") {
    throw 'The package name or SHA-256 checksum does not match the expected .NET 10 release files.'
}

$packageArchive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $entries = @($packageArchive.Entries | Select-Object -ExpandProperty FullName)
    if ($entries.Count -ne 2 -or
        $entries -notcontains 'Lidarr.Plugin.UnwatchSingles.dll' -or
        $entries -notcontains 'Lidarr.Plugin.UnwatchSingles.pdb') {
        throw "Plugin package does not contain only the plugin assembly and symbols at the archive root."
    }
}
finally {
    $packageArchive.Dispose()
}

$smokeDirectory = Join-Path $repositoryRoot "artifacts/smoke/$([guid]::NewGuid())"
$applicationData = Join-Path $smokeDirectory 'config'
$pluginDirectory = Join-Path $applicationData "plugins/$RepositoryOwner/$RepositoryName"

New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
[System.IO.Compression.ZipFile]::ExtractToDirectory($packagePath, $pluginDirectory)

$ownerArguments = @()
if (!$IsWindows) {
    $ownerArguments = @('--env', "PUID=$(id -u)", '--env', "PGID=$(id -g)")
}

$containerId = $null
try {
    $containerId = docker run --detach --pull missing --publish '127.0.0.1::8686' `
        --mount "type=bind,source=$applicationData,target=/config" @ownerArguments $lidarrImage
    $endpoint = docker port $containerId '8686/tcp'
    $baseUrl = "http://$endpoint/api/v1"

    $apiHeaders = @{}
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    $lastHttpError = $null
    $hostStatus = $null
    $plugins = $null
    $schemas = $null
    $scheduledTasks = $null
    $metadataProviders = $null

    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            if (!$apiHeaders.Count) {
                $configurationPath = Join-Path $applicationData 'config.xml'
                $xml = [xml](Get-Content -LiteralPath $configurationPath -Raw)
                $apiKey = [string]$xml.Config.ApiKey
                if (!$apiKey) {
                    throw 'Lidarr has not generated an API key yet.'
                }
                $apiHeaders = @{ 'X-Api-Key' = $apiKey }
            }

            $hostStatus = Invoke-RestMethod -Uri "$baseUrl/system/status" -Headers $apiHeaders -TimeoutSec 3
            $plugins = Invoke-RestMethod -Uri "$baseUrl/system/plugins" -Headers $apiHeaders -TimeoutSec 3
            $schemas = Invoke-RestMethod -Uri "$baseUrl/metadata/schema" -Headers $apiHeaders -TimeoutSec 3
            $metadataProviders = Invoke-RestMethod -Uri "$baseUrl/metadata" -Headers $apiHeaders -TimeoutSec 3
            $scheduledTasks = Invoke-RestMethod -Uri "$baseUrl/system/task" -Headers $apiHeaders -TimeoutSec 3
            break
        }
        catch {
            $lastHttpError = $_.Exception.Message
            Start-Sleep -Milliseconds 500
        }
    }

    if ($null -eq $hostStatus -or $null -eq $plugins -or $null -eq $schemas -or
        $null -eq $metadataProviders -or $null -eq $scheduledTasks) {
        throw "Pinned Lidarr did not expose its plugin, metadata schema, and task APIs in time. Last error: $lastHttpError"
    }

    if ($hostStatus.Version -ne $lidarrVersion -or $hostStatus.RuntimeVersion -ne $lidarrRuntimeVersion -or
        $hostStatus.PackageVersion -ne $lidarrPackageVersion -or !$hostStatus.IsDocker -or
        $hostStatus.AppData -ne '/config' -or $hostStatus.StartupPath -ne '/app/lidarr/bin') {
        throw "Unexpected Lidarr host: $($hostStatus | ConvertTo-Json -Depth 4 -Compress)"
    }
    Write-Output "Verified Lidarr $lidarrVersion ($lidarrPackageVersion) on .NET $lidarrRuntimeVersion."

    $plugin = @($plugins | Where-Object { $_.GithubUrl -eq "https://github.com/$RepositoryOwner/$RepositoryName" }) |
        Select-Object -First 1
    if ($null -eq $plugin -or $plugin.Name -ne 'Unwatch Singles') {
        throw 'Lidarr did not load Unwatch Singles from the installed package.'
    }

    $metadataProvider = @($schemas | Where-Object { $_.Implementation -eq 'UnwatchSinglesMetadata' }) |
        Select-Object -First 1
    if ($null -eq $metadataProvider) {
        throw 'Lidarr did not discover the Unwatch Singles metadata settings provider.'
    }

    $scanTask = @($scheduledTasks | Where-Object {
        $_.TaskName -eq 'ScanDuplicateSingles' -or $_.Name -eq 'Scan Duplicate Singles'
    }) | Select-Object -First 1
    if ($null -eq $scanTask -or $scanTask.Interval -ne 0) {
        throw 'Lidarr did not register the duplicate singles task with its schedule disabled.'
    }

    $provider = @($metadataProviders | Where-Object { $_.Implementation -eq 'UnwatchSinglesMetadata' }) |
        Select-Object -First 1
    if ($null -eq $provider) {
        throw 'Lidarr did not persist the Unwatch Singles metadata provider.'
    }

    if ($provider.Enable -or
        !$provider.Fields.Where({ $_.Name -eq 'dryRun' -and $_.Value -eq $false }) -or
        !$provider.Fields.Where({ $_.Name -eq 'automaticScanningEnabled' -and $_.Value -eq $true }) -or
        !$provider.Fields.Where({ $_.Name -eq 'scheduledScanningEnabled' -and $_.Value -eq $true }) -or
        !$provider.Fields.Where({ $_.Name -eq 'scheduledIntervalHours' -and $_.Value -eq 24 }) -or
        !$provider.Fields.Where({ $_.Name -eq 'durationToleranceMilliseconds' -and $_.Value -eq 3000 })) {
        throw 'The metadata provider did not start disabled with the documented default settings.'
    }

    $pluginVersion = [string]$plugin.InstalledVersion
    $expectedVersionParts = ($ExpectedPluginVersion -split '\+', 2)[0] -split '-', 2
    $expectedVersionCore = $expectedVersionParts[0]
    $expectedPrerelease = if ($expectedVersionParts.Count -gt 1) {
        ' \(' + [regex]::Escape($expectedVersionParts[1]) + '\)'
    }
    else {
        ''
    }
    $pluginVersionPattern = '^' + [regex]::Escape($expectedVersionCore) + '(?:\.0)?' + $expectedPrerelease + '$'
    if ($pluginVersion -notmatch $pluginVersionPattern) {
        throw "Lidarr reported unexpected embedded plugin version '$pluginVersion'."
    }

    # Turn on the native provider in the isolated library. It has no artists, so scans remain harmless.
    $provider.Enable = $true
    $providerJson = ConvertTo-Json -InputObject $provider -Depth 12 -Compress
    Invoke-RestMethod -Uri "$baseUrl/metadata/$($provider.Id)" -Method Put `
        -Headers $apiHeaders -ContentType 'application/json' -Body $providerJson -TimeoutSec 10 | Out-Null
    $metadataProviders = Invoke-RestMethod -Uri "$baseUrl/metadata" -Headers $apiHeaders -TimeoutSec 10
    $provider = @($metadataProviders | Where-Object { $_.Implementation -eq 'UnwatchSinglesMetadata' }) |
        Select-Object -First 1
    $scheduledTasks = Invoke-RestMethod -Uri "$baseUrl/system/task" -Headers $apiHeaders -TimeoutSec 10
    $scanTask = @($scheduledTasks | Where-Object {
        $_.TaskName -eq 'ScanDuplicateSingles' -or $_.Name -eq 'Scan Duplicate Singles'
    }) | Select-Object -First 1

    if (!$provider.Enable -or $scanTask.Interval -ne 1440) {
        throw "Enabling the provider did not register a 24-hour scan (interval: $($scanTask.Interval))."
    }

    $commandBody = @{ name = 'ScanDuplicateSingles'; dryRun = $true } | ConvertTo-Json -Compress
    $submittedCommand = Invoke-RestMethod -Uri "$baseUrl/command" -Method Post `
        -Headers $apiHeaders -ContentType 'application/json' -Body $commandBody -TimeoutSec 10
    $commandId = [int]$submittedCommand.Id
    $commandDeadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $completedCommand = Invoke-RestMethod -Uri "$baseUrl/command/$commandId" `
            -Headers $apiHeaders -TimeoutSec 5
        if ([string]$completedCommand.Status -in @('completed', 'failed')) {
            break
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $commandDeadline)

    if ([string]$completedCommand.Status -ne 'completed') {
        throw "Manual dry-run command did not complete successfully: $($completedCommand | ConvertTo-Json -Depth 5 -Compress)"
    }

    $scheduledTasks = Invoke-RestMethod -Uri "$baseUrl/system/task" -Headers $apiHeaders -TimeoutSec 10
    $scanTask = $scheduledTasks | Where-Object { $_.TaskName -eq 'ScanDuplicateSingles' -or $_.Name -eq 'Scan Duplicate Singles' } |
        Select-Object -First 1
    $expectedLastExecution = [string]$scanTask.LastExecution
    $expectedLastStartTime = [string]$scanTask.LastStartTime
    if (!$expectedLastExecution -or !$expectedLastStartTime) {
        throw 'Lidarr did not report execution timestamps for the registered daily task.'
    }

    # Keep the test config mounted while restarting Lidarr.
    docker restart $containerId | Out-Null
    $endpoint = docker port $containerId '8686/tcp'
    $baseUrl = "http://$endpoint/api/v1"
    $restartDeadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    $restartedTask = $null
    $lastHttpError = $null

    while ([DateTime]::UtcNow -lt $restartDeadline) {
        try {
            $restartedTasks = Invoke-RestMethod -Uri "$baseUrl/system/task" `
                -Headers $apiHeaders -TimeoutSec 3
            $restartedTask = @($restartedTasks | Where-Object {
                $_.TaskName -eq 'ScanDuplicateSingles' -or $_.Name -eq 'Scan Duplicate Singles'
            }) | Select-Object -First 1
            if ($null -ne $restartedTask) {
                break
            }
        }
        catch {
            $lastHttpError = $_.Exception.Message
        }
        Start-Sleep -Milliseconds 500
    }

    if ($null -eq $restartedTask -or
        $restartedTask.Interval -ne 1440 -or
        [string]$restartedTask.LastExecution -ne $expectedLastExecution -or
        [string]$restartedTask.LastStartTime -ne $expectedLastStartTime) {
        throw "The daily task or its execution timestamps were not preserved across restart. Task: $($restartedTask | ConvertTo-Json -Depth 5 -Compress). Last error: $lastHttpError"
    }

    Write-Output 'Lidarr plugin load smoke test passed.'
    Write-Output "Loaded plugin: $($plugin.Name) $pluginVersion."
    Write-Output 'Settings defaults, manual dry-run execution, 24-hour scheduling, and restart timestamp preservation passed.'
}
catch {
    if ($containerId) {
        docker logs --tail 100 $containerId | Out-Host
    }
    throw
}
finally {
    if ($containerId) {
        docker rm --force --volumes $containerId | Out-Null
    }
}
