# Lidarr AI Fork

**Current version: 3.2.1.5196-ai.9** · Linux amd64 · Based on upstream [Lidarr v3.2.1.5196](https://github.com/Lidarr/Lidarr/tree/v3.2.1.5196).

Manual track-picker recovery. This fork adds optional AI identification and the features listed below while retaining upstream licensing.

## Features

- LM Studio album/track identification with Review and Automatic modes, strict result validation and configurable request settings.
- Native release-type search buttons on artist pages.
- Persistent saved AI decisions for Manual Import previews, with explicit fresh mapping.
- Retry Matching is separate from Refresh; unavailable-provider warning spam is suppressed without deleting configuration.
- Complete-release protection and red errors versus yellow review warnings.
- Bounded, staged AI requests and adaptive context/output-limit subdivision.
- Settings-selectable Original and LM Studio identification with native score and AI settings.
- Optional native Unique Singles cleanup, a separate Singles Review page, and monitoring restoration when coverage disappears.
- Queue AI disposition summary and configurable scheduled refresh interval.
- Manual track selection works when no release was identified.

## Download and install

Get the Docker archive, original build source, installation files, checksums and verification records from the [ai.9 release](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.9). Load the archive with `docker load -i <archive>` and use image tag `lidarr-ai:3.2.1.5196-ai.9`. Preserve your existing appdata, media paths and database settings. These are downloadable Docker archives; no container registry image is implied.

- Linux amd64 image, based on upstream Lidarr v3.2.1.5196 and the pinned LinuxServer nightly runtime. Assembly version remains 3.2.1.5196 for plugin compatibility.
- Preserve /config, media mappings, ports, network and database configuration when replacing the container.
- This version includes migration 82 for Singles Review. Unique Singles starts off; disable the external plugin’s cleanup actions before enabling the native feature.
- Back up appdata and any external PostgreSQL databases before upgrading; rollback across migrations can require a database restore. Historical releases can lack later fixes and should not be used as a casual downgrade path.
- Test counts describe each original build’s recorded verification, not a fresh rerun during this repository reorganization. Linux container execution and third-party plugin behavior require runtime verification.

## Release history

| Release | Main changes | Details |
| --- | --- | --- |
| [ai.9](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.9) | Manual track-picker recovery | [Changelog and features](docs/releases/ai.9.md) |
| [ai.8](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.8) | Queue AI disposition and refresh settings | [Changelog and features](docs/releases/ai.8.md) |
| [ai.7](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.7) | Matching settings and native Unique Singles | [Changelog and features](docs/releases/ai.7.md) |
| [ai.6](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.6) | Staged AI matching | [Changelog and features](docs/releases/ai.6.md) |
| [ai.5](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.5) | Complete-release protection and queue severity | [Changelog and features](docs/releases/ai.5.md) |
| [ai.4](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.4) | Dedicated AI retry and quieter provider warnings | [Changelog and features](docs/releases/ai.4.md) |
| [ai.3](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.3) | Saved AI previews and explicit retry | [Changelog and features](docs/releases/ai.3.md) |
| [ai.2](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.2) | Native release-type search | [Changelog and features](docs/releases/ai.2.md) |
| [ai.1](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.1) | AI-assisted music identification | [Changelog and features](docs/releases/ai.1.md) |

See [CHANGELOG.md](CHANGELOG.md) for version-by-version changes and [FEATURES.md](FEATURES.md) for this version’s cumulative feature summary. Earlier releases are historical snapshots and may lack later safeguards.

## Source and licensing

Application source is preserved from each original build snapshot; this cleaned repository history adds publication documentation without rebuilding the binaries. Original build source archives and metadata are attached to the corresponding releases. [Original build README](docs/ORIGINAL-README.md) and [license](LICENSE.md) are retained. Native Unique Singles follows [gergc/lidarr-plugin-uniquesingles](https://github.com/gergc/lidarr-plugin-uniquesingles); its MIT notice is retained under LICENSES.
