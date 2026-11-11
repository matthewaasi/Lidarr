# Lidarr 3.2.1.5196-ai.3 — Saved AI previews and explicit retry

## Changes since ai.2

- Opening Manual Import now reads saved AI proposals without making an inference request; explicit Map Tracks still requests fresh identification.
- Added atomic persistent preview caching with file size/mtime and metadata validation; automatic imports do not reuse preview decisions.
- In this release only, Queue Refresh explicitly retries completed held downloads. This temporary control behavior is replaced by a dedicated button in ai.4.
- Kept scheduled polling from forcing retries of held identifications and excluded imported, ignored, failed-download and active-import items.

## Feature summary

- LM Studio album/track identification with Review and Automatic modes, strict result validation and configurable request settings.
- Native release-type search buttons on artist pages.
- Persistent saved AI decisions for Manual Import previews, with explicit fresh mapping.
- Queue Refresh explicitly retries held matching in this historical release; ai.4 replaces this with a separate action.

## Install on Unraid

Download `lidarr-ai-3.2.1.5196-ai.3-amd64.tar.gz`, copy it to your server, and load it:

```sh
docker load -i /path/to/lidarr-ai-3.2.1.5196-ai.3-amd64.tar.gz
```

Set the existing container’s Repository to `lidarr-ai:3.2.1.5196-ai.3`, preserve its mappings/settings, apply, and hard-refresh the browser. Read the included installation instructions before enabling automatic operations.

## Compatibility and historical limitations

- Linux amd64 image, based on upstream Lidarr v3.2.1.5196 and the pinned LinuxServer nightly runtime. Assembly version remains 3.2.1.5196 for plugin compatibility.
- Preserve /config, media mappings, ports, network and database configuration when replacing the container.
- Back up appdata and any external PostgreSQL databases before upgrading; rollback across migrations can require a database restore. Historical releases can lack later fixes and should not be used as a casual downgrade path.
- Known historical limitation: the strict missing-track safeguard and red/yellow AI error classification were added in ai.5.
- Known historical limitation: staged AI requests were added in ai.6; large prompts in this version can exceed the model context.
- Queue Refresh retries matching in ai.3. Later releases separate that operation from list refresh.
- Test counts describe each original build’s recorded verification, not a fresh rerun during this repository reorganization. Linux container execution and third-party plugin behavior require runtime verification.

## Verification and provenance

52 backend and 12 frontend tests passed.

Original build date: 2026-11-09T19:15:27.353Z. The Docker image and `lidarr-ai-3.2.1.5196-ai.3-build-source.tar.gz` are the original build artifacts; their bytes are checked against the original checksum records. GitHub’s automatic source downloads contain the matching application snapshot plus the reorganized publication documentation. New publication commits/tags replace the former mixed repository history; original build metadata is retained for provenance. No application code was rebuilt or changed during this reorganization.
