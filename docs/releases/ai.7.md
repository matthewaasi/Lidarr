# Lidarr 3.2.1.5196-ai.7 — Matching settings and native Unique Singles

## Changes since ai.6

- Added Settings → Identification with Original Lidarr or LM Studio engines, configurable native album/track scores (80%/60% defaults), and saved LM Studio settings.
- Restored native matching and fingerprint retry without LM Studio requests; changing engines alone does not retry held downloads. Renamed the explicit queue action to Retry Matching.
- Added optional native Unique Singles cleanup, off by default, based on the gergc fork with MIT attribution. Every single track must have downloaded monitored album/EP coverage.
- Added recording-ID and optional title/duration matching, exclusive-track protection, version-conflict review, file ownership checks and normal recycle-bin deletion.
- Added Activity → Singles Review with per-track evidence, confirmation, approve/keep/recheck actions, stale-evidence protection and persistent monitoring ownership.
- Added automatic re-monitoring when coverage disappears while preserving alternative coverage and manual choices; added main-database migration 82.

## Feature summary

- LM Studio album/track identification with Review and Automatic modes, strict result validation and configurable request settings.
- Native release-type search buttons on artist pages.
- Persistent saved AI decisions for Manual Import previews, with explicit fresh mapping.
- Retry Matching is separate from Refresh; unavailable-provider warning spam is suppressed without deleting configuration.
- Complete-release protection and red errors versus yellow review warnings.
- Bounded, staged AI requests and adaptive context/output-limit subdivision.
- Settings-selectable Original and LM Studio identification with native score and AI settings.
- Optional native Unique Singles cleanup, a separate Singles Review page, and monitoring restoration when coverage disappears.

## Install on Unraid

Download `lidarr-ai-3.2.1.5196-ai.7-amd64.tar.gz`, copy it to your server, and load it:

```sh
docker load -i /path/to/lidarr-ai-3.2.1.5196-ai.7-amd64.tar.gz
```

Set the existing container’s Repository to `lidarr-ai:3.2.1.5196-ai.7`, preserve its mappings/settings, apply, and hard-refresh the browser. Read the included installation instructions before enabling automatic operations.

## Compatibility and historical limitations

- Linux amd64 image, based on upstream Lidarr v3.2.1.5196 and the pinned LinuxServer nightly runtime. Assembly version remains 3.2.1.5196 for plugin compatibility.
- Preserve /config, media mappings, ports, network and database configuration when replacing the container.
- This version includes migration 82 for Singles Review. Unique Singles starts off; disable the external plugin’s cleanup actions before enabling the native feature.
- Back up appdata and any external PostgreSQL databases before upgrading; rollback across migrations can require a database restore. Historical releases can lack later fixes and should not be used as a casual downgrade path.
- Test counts describe each original build’s recorded verification, not a fresh rerun during this repository reorganization. Linux container execution and third-party plugin behavior require runtime verification.

## Verification and provenance

121 backend and 26 frontend tests passed; isolated API/startup checks and PostgreSQL SQL-generation checks passed.

Original build date: 2026-11-09T22:41:52.572Z. The Docker image and `lidarr-ai-3.2.1.5196-ai.7-build-source.tar.gz` are the original build artifacts; their bytes are checked against the original checksum records. GitHub’s automatic source downloads contain the matching application snapshot plus the reorganized publication documentation. New publication commits/tags replace the former mixed repository history; original build metadata is retained for provenance. No application code was rebuilt or changed during this reorganization.
