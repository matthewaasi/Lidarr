# Lidarr 3.2.1.5196-ai.10 — Native-first identification with AI fallback

## Changes since ai.9

- Added the selectable Lidarr first, LM Studio fallback engine while preserving existing engine selections until changed in Settings.
- Runs native matching and allowed fingerprinting first; successful identification and new-download completeness require no AI request.
- Sends a failed identification’s whole release to LM Studio under its existing confidence and Review/Automatic settings.
- Keeps quality, duplicate and path/import failures from triggering AI fallback; an AI error never restores a weak native result.
- Manual preview tries native matching and reuses saved AI results without calling the model. Explicit mapping and Retry Matching perform fresh hybrid identification; no new migration.

## Feature summary

- LM Studio album/track identification with Review and Automatic modes, strict result validation and configurable request settings.
- Native release-type search buttons on artist pages.
- Persistent saved AI decisions for Manual Import previews, with explicit fresh mapping.
- Retry Matching is separate from Refresh; unavailable-provider warning spam is suppressed without deleting configuration.
- Complete-release protection and red errors versus yellow review warnings.
- Bounded, staged AI requests and adaptive context/output-limit subdivision.
- Settings-selectable Original, LM Studio, and native-first/AI-fallback identification with native score and AI settings.
- Optional native Unique Singles cleanup, a separate Singles Review page, and monitoring restoration when coverage disappears.
- Queue AI disposition summary and configurable scheduled refresh interval.
- Manual track selection works when no release was identified.

## Install on Unraid

Download `lidarr-ai-3.2.1.5196-ai.10-amd64.tar.gz`, copy it to your server, and load it:

```sh
docker load -i /path/to/lidarr-ai-3.2.1.5196-ai.10-amd64.tar.gz
```

Set the existing container’s Repository to `lidarr-ai:3.2.1.5196-ai.10`, preserve its mappings/settings, apply, and hard-refresh the browser. Read the included installation instructions before enabling automatic operations.

## Compatibility and historical limitations

- Linux amd64 image, based on upstream Lidarr v3.2.1.5196 and the pinned LinuxServer nightly runtime. Assembly version remains 3.2.1.5196 for plugin compatibility.
- Preserve /config, media mappings, ports, network and database configuration when replacing the container.
- This version includes migration 82 for Singles Review. Unique Singles starts off; disable the external plugin’s cleanup actions before enabling the native feature.
- Back up appdata and any external PostgreSQL databases before upgrading; rollback across migrations can require a database restore. Historical releases can lack later fixes and should not be used as a casual downgrade path.
- Test counts describe each original build’s recorded verification, not a fresh rerun during this repository reorganization. Linux container execution and third-party plugin behavior require runtime verification.

## Verification and provenance

87 backend and 33 frontend tests passed. Production frontend and Linux publishing passed; backend analyzers were disabled for this build.

Original build date: 2026-11-10T14:55:35.137Z. The Docker image and `lidarr-ai-3.2.1.5196-ai.10-build-source.tar.gz` are the original build artifacts; their bytes are checked against the original checksum records. GitHub’s automatic source downloads contain the matching application snapshot plus the reorganized publication documentation. New publication commits/tags replace the former mixed repository history; original build metadata is retained for provenance. No application code was rebuilt or changed during this reorganization.
