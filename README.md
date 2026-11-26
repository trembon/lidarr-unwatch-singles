# Lidarr Unwatch Singles

Unwatch Singles keeps redundant single releases from being monitored when the same recordings already appear on one of the artist's monitored albums or EPs. It only changes the monitoring state of a Single.

Unwatch Singles 0.2 targets .NET 10 and requires Lidarr `4.0.0.5236` or later with plugin support. The build and load checks use LinuxServer's nightly image `4.0.0.5236-ls228` with .NET `10.0.15`. For Lidarr on .NET 8, use the [v0.0.1 release](https://github.com/trembon/lidarr-unwatch-singles/releases/tag/v0.0.1).

## Install

1. Open **System → Plugins** in Lidarr.
2. Enter `https://github.com/trembon/lidarr-unwatch-singles`.
3. Install the plugin and restart Lidarr. Lidarr selects a release that matches its .NET version.
4. Open **Settings → Metadata** and edit **Unwatch Singles**. Turn on **Dry run** when enabling the provider for the first time.
5. Start **Scan Duplicate Singles** from **System → Tasks**. Review the scan totals and the Lidarr log before turning Dry run off.

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

Install the .NET SDK pinned in `global.json`, PowerShell 7.4 or later, and Docker with Linux containers enabled. From PowerShell at the repository root, extract the Lidarr references and run the tests:

```powershell
$referencePath = .\scripts\Get-LidarrReferences.ps1
dotnet restore .\Lidarr.UnwatchSingles.sln --configfile .\NuGet.Config
dotnet test .\Lidarr.UnwatchSingles.sln -c Release --no-restore "-p:LidarrReferencePath=$referencePath"
```

The helper uses the digest-pinned LinuxServer image and copies the build references from `/app/lidarr/bin` into the ignored `artifacts/` directory. The plugin references those assemblies at build time and uses Lidarr's copies at runtime.

Create an installable archive locally with:

```powershell
.\scripts\Package-Plugin.ps1 `
  -RepositoryOwner trembon `
  -RepositoryName lidarr-unwatch-singles `
  -LidarrReferencePath $referencePath
```

The package script defaults to version `0.2.0` and builds `Lidarr.Plugin.UnwatchSingles.net10.0.zip` and its SHA-256 sidecar beneath `artifacts/release/`. The archive contains the plugin DLL and symbols at its root.

Test the package in a container with a disposable configuration directory:

```powershell
.\scripts\Test-LidarrLoad.ps1 `
  -PackagePath .\artifacts\release\Lidarr.Plugin.UnwatchSingles.net10.0.zip `
  -RepositoryOwner trembon `
  -RepositoryName lidarr-unwatch-singles
```

## GitHub builds and releases

GitHub Actions runs tests, verifies the archive contents and checksum, and loads the package in the pinned LinuxServer container. The smoke test verifies the metadata provider, manual dry run, and daily task persistence across a restart. These checks run for pull requests, pushes to `main`, and version tags.

To publish a release, merge the tested change into `main`, then create and push a semantic version tag:

```powershell
git tag v0.2.0
git push origin v0.2.0
```

Only tags of the form `vMAJOR.MINOR.PATCH` publish a release. The release contains the installable .NET 10 ZIP and its SHA-256 sidecar and targets `main` for Lidarr's plugin installer.

## Troubleshooting

- **Lidarr does not show Plugins**. Use a plugin-capable .NET 10 build at or above `4.0.0.5236` for version 0.2, then restart Lidarr after installing.
- **The plugin cannot load**. Check that the archive came from the matching `.net10.0.zip` release and look for the first plugin load error in the Lidarr log.
- **No provider appears:** open **Settings → Metadata** after the plugin loads and enable the Unwatch Singles provider.
- **A title match is only marked ambiguous:** the duration is missing or outside tolerance. The plugin intentionally leaves that Single monitored.
- **A Single is skipped:** confirm that its parent album and one edition are monitored, its track list is complete, and all tracks match one monitored edition of the same artist's Album or EP.

## Architecture notes

See [Lidarr integration notes](docs/lidarr-architecture.md) for the host interfaces and packaging details. The original implementation drew on the integration points and matching tiers of the [UniqueSingles project](https://github.com/jtstothard/lidarr-plugin-uniquesingles).
