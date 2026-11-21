# Lidarr AI Fork

**Current version: 3.2.1.5196-ai.20** · Linux amd64 · Based on upstream [Lidarr v3.2.1.5196](https://github.com/Lidarr/Lidarr/tree/v3.2.1.5196).

Album-wide AI reconciliation resolves conflicting file-stage release choices before remapping tracks. Unmatched extra files no longer erase an otherwise identified release. Prior search-performance improvements remain included. This fork adds optional AI identification and the features listed below while retaining upstream licensing.

## Features

- Separate automatic and manual identification methods, with manual matching inheriting the automatic method by default.

- Album-wide AI reconciliation for conflicting or uncertain file batches, followed by fresh mapping against the selected release, with confidence and duplicate-track validation.

- Batched debug/trace file writes, per-search artist/album indexes, reused criteria regexes, and periodic/stopped-search timing summaries. Matching thresholds and candidate coverage are retained.

- Faster search evaluation through per-search cached reads and matching, bounded workers, stable result order, and full interactive rejection details. Configure `LIDARR_RELEASE_WORKERS` (1–16; default up to four CPUs).

- Audit-page phase percentages, completed/total file counters, activity and update timestamps, with automatic progress reconnection after reopening.

- Interactive search lists outstanding indexers, processing release X/Y and final ranking. System → Tasks supports cooperative cancellation for running searches and Library Audit.

- Progressive interactive search with checked result batches, processed/total counts, cancellation on close, and pages of 100 results. Matching rules and final ranking are unchanged.

- LM Studio album/track identification with Review and Automatic modes, strict result validation and configurable request settings.
- Native release-type search buttons on artist pages.
- Persistent saved AI decisions for Manual Import previews, with explicit fresh mapping.
- Retry Matching is separate from Refresh; unavailable-provider warning spam is suppressed without deleting configuration.
- Complete-release protection and red errors versus yellow review warnings.
- Bounded, staged AI requests and adaptive context/output-limit subdivision.
- Settings-selectable Original, LM Studio, and native-first/AI-fallback identification with native score and AI settings.
- Optional native Unique Singles cleanup, a separate Singles Review page, and monitoring restoration when coverage disappears.
- Queue AI disposition summary and a seconds-based scheduled refresh interval (1–86,400 seconds; default 60). Existing minute-based settings retain their duration.
- Manual track selection works when no release was identified.
- Library Audit with persistent review, optional fingerprint evidence, exact-file duplicate checks, finding deletion and optional file removal with previews and duplicate-copy protection.

## Download and install

The ai.20 build package contains a Docker-loadable archive, matching source, installation notes, checksums and verification records. Load the archive with `docker load -i <archive>` and use image tag `lidarr-ai:3.2.1.5196-ai.20`. See [ai.20 notes](docs/releases/ai.20.md). Preserve your existing appdata, media paths and database settings. These are downloadable Docker archives; no container registry image is implied.

- Linux amd64 image, based on upstream Lidarr v3.2.1.5196 and the pinned LinuxServer nightly runtime. Assembly version remains 3.2.1.5196 for plugin compatibility.
- Preserve /config, media mappings, ports, network and database configuration when replacing the container.
- This version includes migration 82 for Singles Review. Unique Singles starts off; disable the external plugin’s cleanup actions before enabling the native feature.
- Migration 83 adds Library Audit findings. Audit scans do not change music or monitoring. File removal requires explicit selection, a preview and confirmation.
- Back up appdata and any external PostgreSQL databases before upgrading; rollback across migrations can require a database restore. Historical releases can lack later fixes and should not be used as a casual downgrade path.
- Test counts describe each original build’s recorded verification, not a fresh rerun during this repository reorganization. Linux container execution and third-party plugin behavior require runtime verification.

## Release history

GitHub publication tags use `ai.01`–`ai.09` so releases sort correctly. Docker image tags and filenames retain `ai.1`–`ai.9`. GitHub shows ten releases per page; [ai.1 is on page 2](https://github.com/matthewaasi/Lidarr/releases?page=2).

| Release | Main changes | Details |
| --- | --- | --- |
| [ai.20](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.20) | Separate automatic and manual identification methods | [Release notes](docs/releases/ai.20.md) |
| [ai.19](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.19) | Album-wide AI release reconciliation and constrained remapping | [Changelog and features](docs/releases/ai.19.md) |
| [ai.18](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.18) | Batched logging, indexed matching and periodic search timings | [Changelog and features](docs/releases/ai.18.md) |
| [ai.17](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.17) | Search caches, bounded workers and processing timings | [Changelog and features](docs/releases/ai.17.md) |
| [ai.16](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.16) | Live Audit-page phase progress and file counters | [Changelog and features](docs/releases/ai.16.md) |
| [ai.15](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.15) | Running task cancellation and named search progress | [Changelog and features](docs/releases/ai.15.md) |
| [ai.14](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.14) | Progressive interactive search and cancellation | [Changelog and features](docs/releases/ai.14.md) |
| [ai.13](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.13) | Audit deletion, file previews and duplicate-copy protection | [Changelog and features](docs/releases/ai.13.md) |
| [ai.12](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.12) | Queue refresh interval entered in seconds | [Changelog and features](docs/releases/ai.12.md) |
| [ai.11](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.11) | Library Audit and faster library pages | [Changelog and features](docs/releases/ai.11.md) |
| [ai.10](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.10) | Native-first identification with AI fallback | [Changelog and features](docs/releases/ai.10.md) |
| [ai.9](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.09) | Manual track-picker recovery | [Changelog and features](docs/releases/ai.9.md) |
| [ai.8](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.08) | Queue AI disposition and refresh settings | [Changelog and features](docs/releases/ai.8.md) |
| [ai.7](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.07) | Matching settings and native Unique Singles | [Changelog and features](docs/releases/ai.7.md) |
| [ai.6](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.06) | Staged AI matching | [Changelog and features](docs/releases/ai.6.md) |
| [ai.5](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.05) | Complete-release protection and queue severity | [Changelog and features](docs/releases/ai.5.md) |
| [ai.4](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.04) | Dedicated AI retry and quieter provider warnings | [Changelog and features](docs/releases/ai.4.md) |
| [ai.3](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.03) | Saved AI previews and explicit retry | [Changelog and features](docs/releases/ai.3.md) |
| [ai.2](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.02) | Native release-type search | [Changelog and features](docs/releases/ai.2.md) |
| [ai.1](https://github.com/matthewaasi/Lidarr/releases/tag/v3.2.1.5196-ai.01) | AI-assisted music identification | [Changelog and features](docs/releases/ai.1.md) |

See [CHANGELOG.md](CHANGELOG.md) for version-by-version changes and [FEATURES.md](FEATURES.md) for this version’s cumulative feature summary. Earlier releases are historical snapshots and may lack later safeguards.

## Source and licensing

ai.20 builds new application binaries from this source. Releases through ai.11 preserve their original build snapshots; their publication reorganization did not rebuild binaries. Original build source archives and metadata are attached to the corresponding releases. [Original build README](docs/ORIGINAL-README.md) and [license](LICENSE.md) are retained. Native Unique Singles follows [gergc/lidarr-plugin-uniquesingles](https://github.com/gergc/lidarr-plugin-uniquesingles); its MIT notice is retained under LICENSES.
