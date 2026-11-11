# Lidarr AI Fork

**Current version: 3.2.1.5196-ai.1** · Linux amd64 · Based on upstream [Lidarr v3.2.1.5196](https://github.com/Lidarr/Lidarr/tree/v3.2.1.5196).

AI-assisted music identification. This fork adds optional AI identification and the features listed below while retaining upstream licensing.

## Features

- LM Studio album/track identification with Review and Automatic modes, strict result validation and configurable request settings.

## Download and install

Get the Docker archive, original build source, installation files, checksums and verification records from the [ai.1 release](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.1). Load the archive with `docker load -i <archive>` and use image tag `lidarr-ai:3.2.1.5196-ai.1`. Preserve your existing appdata, media paths and database settings. These are downloadable Docker archives; no container registry image is implied.

- Linux amd64 image, based on upstream Lidarr v3.2.1.5196 and the pinned LinuxServer nightly runtime. Assembly version remains 3.2.1.5196 for plugin compatibility.
- Preserve /config, media mappings, ports, network and database configuration when replacing the container.
- Back up appdata and any external PostgreSQL databases before upgrading; rollback across migrations can require a database restore. Historical releases can lack later fixes and should not be used as a casual downgrade path.
- Known historical limitation: the strict missing-track safeguard and red/yellow AI error classification were added in ai.5.
- Known historical limitation: staged AI requests were added in ai.6; large prompts in this version can exceed the model context.
- Test counts describe each original build’s recorded verification, not a fresh rerun during this repository reorganization. Linux container execution and third-party plugin behavior require runtime verification.

## Release history

| Release | Main changes | Details |
| --- | --- | --- |
| [ai.1](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.1) | AI-assisted music identification | [Changelog and features](docs/releases/ai.1.md) |

See [CHANGELOG.md](CHANGELOG.md) for version-by-version changes and [FEATURES.md](FEATURES.md) for this version’s cumulative feature summary. Earlier releases are historical snapshots and may lack later safeguards.

## Source and licensing

Application source is preserved from each original build snapshot; this cleaned repository history adds publication documentation without rebuilding the binaries. Original build source archives and metadata are attached to the corresponding releases. [Original build README](docs/ORIGINAL-README.md) and [license](LICENSE.md) are retained.
