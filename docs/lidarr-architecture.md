# Lidarr integration notes

This document records the Lidarr interfaces verified for the 3.1.6.5078 baseline before monitoring behavior is implemented. The project targets the nightly/develop channel and .NET 8. Stable Lidarr builds that do not load plugins are not supported.

## Loading and dependency injection

Lidarr discovers plugin assemblies whose names start with `Lidarr.Plugin.`. The assembly must export a concrete type deriving from `NzbDrone.Core.Plugins.Plugin`; a `Plugin` supplies a display name, repository owner, and GitHub URL. Lidarr loads the plugin in a collectible `AssemblyLoadContext`, resolves shared Lidarr assemblies from the host, and registers exported plugin types in its DryIoc container. Host assemblies must therefore be referenced at compile time and excluded from the release archive.

The installer queries GitHub releases for the plugin repository. It skips draft releases, requires an asset name containing the host framework (for this baseline, `net8.0.zip`), and extracts that archive beneath Lidarr's plugin directory. The archive contains plugin files at its root. A release body containing `Minimum Lidarr Version: 3.1.6.5078` communicates the supported minimum to the installer.

## Library data and state changes

`IArtistService.GetAllArtists()` enumerates artists, `IAlbumService.GetAlbumsByArtist(artistId)` returns their release groups, and `IReleaseService.GetReleasesByAlbum(albumId)` returns the available editions. `ITrackService.GetTracksByRelease(releaseId)` returns the complete stored track list for one edition. An album's `AlbumType` identifies its primary type; `Album.Monitored` controls the release group, and `AlbumRelease.Monitored` identifies a monitored edition. A `Track` has a title, duration in milliseconds, and MusicBrainz recording ID.

`IAlbumService.SetAlbumMonitored(id, false)` updates one release group's monitoring state and publishes an `AlbumEditedEvent`. It does not delete files. Do not use release or media-file services to change monitoring state.

## Events and commands

`ArtistRefreshCompleteEvent` fires after refreshed artist and album metadata has been saved. `AlbumImportedEvent` contains the artist, album, and imported edition. `AlbumEditedEvent` is published when album monitoring changes. Synchronous `IHandle<TEvent>` handlers can start artist-scoped scans. The handler reacts to Album and EP edits and ignores Single edits, so it does not react to the plugin's own Single unmonitoring update.

Public types deriving from `Command` are visible in the task UI when Lidarr discovers a public `IExecute<TCommand>` handler. `IScheduledTaskRepository` stores a task by command type name, while `ICacheManager.GetCache<ScheduledTask>(typeof(TaskManager))` synchronizes the scheduler's cache. Lidarr's startup task initialization removes plugin-owned task rows. Preserve an existing row's timestamps before that cleanup, then reconcile its interval and restore its cache entry from an `ApplicationStartedEvent` handler ordered after Lidarr's task manager.

## Settings and logging

A class derived from `MetadataBase<TSettings>` exposes one plugin entry under Settings → Metadata using `FieldDefinition` attributes and an `IProviderConfig` implementation. The persisted `MetadataDefinition.Enable` setting is the plugin's main on/off switch. Metadata providers must implement the metadata-output methods; this plugin returns no files or images. Normal Lidarr `Logger` instances are injected through the plugin container.

## Build and release constraints

The host, core and common assemblies come from the official `v3.1.6.5078` Linux core distribution. GitHub's official release API supplies that asset's SHA-256 digest. CI verifies the downloaded archive before extracting its assemblies. Build against .NET 8 and package only `Lidarr.Plugin.UnwatchSingles.dll` and its PDB into `Lidarr.Plugin.UnwatchSingles.net8.0.zip`.
