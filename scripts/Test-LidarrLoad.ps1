param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [string]$LidarrDistributionPath,

    [Parameter(Mandatory = $true)]
    [string]$RepositoryOwner,

    [Parameter(Mandatory = $true)]
    [string]$RepositoryName,

    [string]$ExpectedPluginVersion = '1.0.0',

    [ValidateSet('linux-x64', 'win-x64')]
    [string]$RuntimeIdentifier = $(if ($env:OS -eq 'Windows_NT') { 'win-x64' } else { 'linux-x64' }),

    [int]$StartupTimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$packagePath = [System.IO.Path]::GetFullPath($PackagePath, $repositoryRoot)
$lidarrDistributionPath = [System.IO.Path]::GetFullPath($LidarrDistributionPath, $repositoryRoot)

if (!(Test-Path -LiteralPath $packagePath)) {
    throw "Plugin package '$packagePath' does not exist."
}

$applicationDirectory = if (Test-Path -LiteralPath (Join-Path $lidarrDistributionPath 'Lidarr/Lidarr.Core.dll')) {
    Join-Path $lidarrDistributionPath 'Lidarr'
}
else {
    $lidarrDistributionPath
}
$expectedLidarrExecutable = if ($RuntimeIdentifier -eq 'win-x64') { 'Lidarr.exe' } else { 'Lidarr' }
$lidarrExecutable = Join-Path $applicationDirectory $expectedLidarrExecutable

if (!(Test-Path -LiteralPath $lidarrExecutable)) {
    throw "Pinned Lidarr executable '$lidarrExecutable' was not found."
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

$smokeDirectory = Join-Path $repositoryRoot "artifacts/smoke/$RuntimeIdentifier"
$applicationData = Join-Path $smokeDirectory 'data'
$pluginDirectory = Join-Path $applicationData "plugins/$RepositoryOwner/$RepositoryName"
$stdoutPath = Join-Path $smokeDirectory 'lidarr.stdout.log'
$stderrPath = Join-Path $smokeDirectory 'lidarr.stderr.log'

if (Test-Path -LiteralPath $smokeDirectory) {
    $absoluteSmokeDirectory = [System.IO.Path]::GetFullPath($smokeDirectory)
    $absoluteArtifactDirectory = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
    if (!$absoluteSmokeDirectory.StartsWith($absoluteArtifactDirectory,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear smoke data outside '$absoluteArtifactDirectory'."
    }

    Remove-Item -LiteralPath $smokeDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
[System.IO.Compression.ZipFile]::ExtractToDirectory($packagePath, $pluginDirectory)

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
$listener.Stop()

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $lidarrExecutable
$startInfo.WorkingDirectory = $applicationDirectory
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.ArgumentList.Add('/nobrowser')
$startInfo.ArgumentList.Add("/data=$applicationData")
$startInfo.Environment['LIDARR__SERVER__BINDADDRESS'] = '127.0.0.1'
$startInfo.Environment['LIDARR__SERVER__PORT'] = "$port"

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo

try {
    if (!$process.Start()) {
        throw 'Pinned Lidarr did not start.'
    }

    $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
    $standardErrorTask = $process.StandardError.ReadToEndAsync()
    $apiKey = $null
    $apiHeaders = @{}
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    $lastHttpError = $null
    $plugins = $null
    $schemas = $null
    $scheduledTasks = $null
    $metadataProviders = $null

    while ([DateTime]::UtcNow -lt $deadline) {
        if ($process.HasExited) {
            break
        }

        $configurationPath = Join-Path $applicationData 'config.xml'
        if ($null -eq $apiKey -and (Test-Path -LiteralPath $configurationPath)) {
            try {
                $xml = [xml](Get-Content -LiteralPath $configurationPath -Raw)
                $apiKey = [string]$xml.Config.ApiKey
                if ($apiKey) {
                    $apiHeaders = @{ 'X-Api-Key' = $apiKey }
                }
            }
            catch {
                $lastHttpError = $_.Exception.Message
            }
        }

        try {
            $baseUrl = "http://127.0.0.1:$port/api/v1"
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

    if ($null -eq $plugins -or $null -eq $schemas -or $null -eq $metadataProviders -or $null -eq $scheduledTasks) {
        $process.Refresh()
        $status = if ($process.HasExited) { "exit code $($process.ExitCode)" } else { "still starting after $StartupTimeoutSeconds seconds" }
        $capturedStdout = if ($standardOutputTask.IsCompleted) { $standardOutputTask.Result } else { '' }
        $capturedStderr = if ($standardErrorTask.IsCompleted) { $standardErrorTask.Result } else { '' }
        throw "Pinned Lidarr did not expose its plugin, metadata schema, and task APIs ($status). HTTP error: $lastHttpError. stdout: $capturedStdout. stderr: $capturedStderr"
    }

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
    if ($null -eq $scanTask) {
        throw 'Lidarr did not register the scheduled duplicate singles task.'
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

    $expectedLastExecution = [string]$scanTask.LastExecution
    $expectedLastStartTime = [string]$scanTask.LastStartTime

    # Lidarr deletes plugin task rows during startup. Restart the same isolated data directory
    # and verify the plugin restores both its daily interval and execution timestamps.
    $process.Kill($true)
    [void]$process.WaitForExit(10000)
    $process.Dispose()
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (!$process.Start()) {
        throw 'Pinned Lidarr did not restart for the task persistence check.'
    }
    $restartStdoutTask = $process.StandardOutput.ReadToEndAsync()
    $restartStderrTask = $process.StandardError.ReadToEndAsync()
    $restartDeadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    $restartedTask = $null

    while ([DateTime]::UtcNow -lt $restartDeadline -and !$process.HasExited) {
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
            Start-Sleep -Milliseconds 500
        }
    }

    if ($null -eq $restartedTask -or
        $restartedTask.Interval -ne 1440 -or
        [string]$restartedTask.LastExecution -ne $expectedLastExecution -or
        [string]$restartedTask.LastStartTime -ne $expectedLastStartTime) {
        $capturedStdout = if ($restartStdoutTask.IsCompleted) { $restartStdoutTask.Result } else { '' }
        $capturedStderr = if ($restartStderrTask.IsCompleted) { $restartStderrTask.Result } else { '' }
        throw "The daily task or its execution timestamps were not preserved across restart. Task: $($restartedTask | ConvertTo-Json -Depth 5 -Compress). stdout: $capturedStdout. stderr: $capturedStderr"
    }

    Write-Output 'Lidarr plugin load smoke test passed.'
    Write-Output "Loaded plugin: $($plugin.Name) $pluginVersion."
    Write-Output 'Settings defaults, manual dry-run execution, 24-hour scheduling, and restart timestamp preservation passed.'
}
finally {
    if (!$process.HasExited) {
        $process.Kill($true)
        [void]$process.WaitForExit(10000)
    }

    $process.Dispose()
}
