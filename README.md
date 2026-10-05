# Lidarr Unwatch Singles

Unwatch Singles keeps redundant single releases from being monitored when the same recordings already appear on one of the artist's monitored albums or EPs. It only changes the monitoring state of a Single.

The plugin targets plugin-capable Lidarr .NET 8 builds. The build and load checks use Lidarr `3.1.6.5078`. This is a develop/nightly build; use a supported Lidarr build before installing plugins.

## Install

1. Open **System → Plugins** in Lidarr.
2. Enter `https://github.com/trembon/lidarr-unwatch-singles`.
3. Install the plugin and restart Lidarr.
4. Open **Settings → Metadata**, edit **Unwatch Singles**, and enable it.
5. Leave **Dry run** enabled while reviewing the first scan. Start **Scan Duplicate Singles** from **System → Tasks**. Read the scan totals and the Lidarr log; when the results look right, turn Dry run off.

After enabling the provider, artist refreshes and imported albums trigger a scan for that artist. The daily full-library scan catches anything those events missed. Both automatic modes have independent settings.

The **System → Tasks** entry runs a full-library scan. To run a dry run or limit a scan to one artist, send a command through Lidarr's authenticated `POST /api/v1/command` endpoint. Omit `artistId` for the full library:

```json
{
  "name": "ScanDuplicateSingles",
  "artistId": 123,
  "dryRun": true
}
```

## Matching behavior

The plugin compares the selected, monitored Single edition with each selected, monitored Album or EP edition for the same artist. Both parent release groups must be monitored. Downloads and files are not required. A single must have a complete, non-empty track list, and all of its tracks must match distinct tracks on the same Album or EP edition.

An equal MusicBrainz recording ID is the strongest match. Otherwise, normalized titles and durations within the configured tolerance (3,000 ms by default) can match, even when both IDs exist and differ. Unicode, case, repeated whitespace, and punctuation are normalized. Words that describe live, acoustic, or remixed versions are preserved. Matching titles without a confident duration match are logged for review, with no monitoring change.

The Settings → Metadata entry starts disabled. Once enabled, defaults are Dry run off, automatic artist scans on, scheduled full scan on, 24-hour interval, and 3,000 ms duration tolerance. The native provider Enable switch disables both automatic scans and changes from manual and scheduled scans. Enabling dry run reports candidates without changing monitoring state.

## Build and test locally

Install the .NET 8 SDK. From PowerShell at the repository root, obtain the official Lidarr references and run the tests:

```powershell
$referencePath = .\scripts\Get-LidarrReferences.ps1
dotnet restore .\Lidarr.UnwatchSingles.sln --configfile .\NuGet.Config
dotnet test .\Lidarr.UnwatchSingles.sln -c Release --no-restore "-p:LidarrReferencePath=$referencePath"
```

The helper downloads Lidarr `3.1.6.5078` from its official GitHub release, verifies the asset against GitHub's SHA-256 digest, extracts the host assemblies under the ignored `artifacts/` directory, and prints the reference path. The plugin project references Lidarr assemblies without copying them into the plugin build output.

Create an installable archive locally with:

```powershell
.\scripts\Package-Plugin.ps1 `
  -Version 1.0.0 `
  -RepositoryOwner trembon `
  -RepositoryName lidarr-unwatch-singles `
  -LidarrReferencePath $referencePath
```

The package script builds `Lidarr.Plugin.UnwatchSingles.net8.0.zip` and its SHA-256 sidecar beneath `artifacts/release/`. The archive contains only the plugin DLL and symbols at its root.

## GitHub builds and releases

GitHub Actions runs tests, verifies the archive contents, and installs the package into a separate Lidarr data directory to check plugin loading, metadata settings discovery, and scheduled-task registration. These checks run for pull requests, pushes to `main`, and version tags.

To publish a release, merge the tested change into `main`, then create and push a semantic version tag:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

Only tags of the form `vMAJOR.MINOR.PATCH` publish a release. The release contains the installable .NET 8 ZIP and its SHA-256 sidecar and targets `main` for Lidarr's plugin installer.

## Troubleshooting

- **Lidarr does not show Plugins:** use a .NET 8, plugin-capable develop/nightly build at or above `3.1.6.5078`, then restart Lidarr after installing.
- **The plugin cannot load:** check that the archive came from the matching `.net8.0.zip` release, check Lidarr's log for the first plugin load error, and verify the archive was not extracted as an extra nested ZIP directory.
- **No provider appears:** open **Settings → Metadata** after the plugin loads and enable the Unwatch Singles provider.
- **A title match is only marked ambiguous:** the duration is missing or outside tolerance. The plugin intentionally leaves that Single monitored.
- **A Single is skipped:** confirm that its parent album and one edition are monitored, its track list is complete, and all tracks match one monitored edition of the same artist's Album or EP.

## Architecture notes

The pinned Lidarr service and event findings used to design the integration are documented in [docs/lidarr-architecture.md](docs/lidarr-architecture.md). The plugin is an original implementation; the [UniqueSingles project](https://github.com/jtstothard/lidarr-plugin-uniquesingles) informed the integration points and conservative matching tiers.
