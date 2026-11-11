# Lidarr 3.2.1.5196-ai.8 — Queue AI disposition and refresh settings

## Changes since ai.7

- Added the Queue AI Summary column: Approved, Not a match, Issues found or Not assessed.
- Kept Approved as an AI disposition only: Review mode still requires manual import.
- Added Settings → Download Clients → Queue Refresh Interval, configurable from 1–1440 minutes with a one-minute default.
- Updated scheduled polling when the interval is saved, without automatically retrying held AI decisions; no new database migration.

## Feature summary

- LM Studio album/track identification with Review and Automatic modes, strict result validation and configurable request settings.
- Native release-type search buttons on artist pages.
- Persistent saved AI decisions for Manual Import previews, with explicit fresh mapping.
- Retry Matching is separate from Refresh; unavailable-provider warning spam is suppressed without deleting configuration.
- Complete-release protection and red errors versus yellow review warnings.
- Bounded, staged AI requests and adaptive context/output-limit subdivision.
- Settings-selectable Original and LM Studio identification with native score and AI settings.
- Optional native Unique Singles cleanup, a separate Singles Review page, and monitoring restoration when coverage disappears.
- Queue AI disposition summary and configurable scheduled refresh interval.

## Install on Unraid

Download `lidarr-ai-3.2.1.5196-ai.8-amd64.tar.gz`, copy it to your server, and load it:

```sh
docker load -i /path/to/lidarr-ai-3.2.1.5196-ai.8-amd64.tar.gz
```

Set the existing container’s Repository to `lidarr-ai:3.2.1.5196-ai.8`, preserve its mappings/settings, apply, and hard-refresh the browser. Read the included installation instructions before enabling automatic operations.

## Compatibility and historical limitations

- Linux amd64 image, based on upstream Lidarr v3.2.1.5196 and the pinned LinuxServer nightly runtime. Assembly version remains 3.2.1.5196 for plugin compatibility.
- Preserve /config, media mappings, ports, network and database configuration when replacing the container.
- This version includes migration 82 for Singles Review. Unique Singles starts off; disable the external plugin’s cleanup actions before enabling the native feature.
- Back up appdata and any external PostgreSQL databases before upgrading; rollback across migrations can require a database restore. Historical releases can lack later fixes and should not be used as a casual downgrade path.
- Test counts describe each original build’s recorded verification, not a fresh rerun during this repository reorganization. Linux container execution and third-party plugin behavior require runtime verification.

## Verification and provenance

13 focused backend tests passed; changed-file lint and the production frontend build passed. Backend analyzers were disabled for this build because of existing StyleCop issues.

Original build date: 2026-11-09T23:30:16.103Z. The Docker image and `lidarr-ai-3.2.1.5196-ai.8-build-source.tar.gz` are the original build artifacts; their bytes are checked against the original checksum records. GitHub’s automatic source downloads contain the matching application snapshot plus the reorganized publication documentation. New publication commits/tags replace the former mixed repository history; original build metadata is retained for provenance. No application code was rebuilt or changed during this reorganization.
