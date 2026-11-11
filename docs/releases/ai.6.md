# Lidarr 3.2.1.5196-ai.6 — Staged AI matching

## Changes since ai.5

- Split large candidate sets, file batches and long track lists into bounded requests, then compared proposals before applying a combined decision.
- Preserved global identifiers and rejected duplicate assignments across stages; later-stage failure cannot apply a partial success.
- Added adaptive subdivision for recognized context-limit errors and smaller file batches for truncated output.
- Added configurable defaults of eight files and an 18,000-character request target, short-reason instructions, stage logging and bounded request/depth limits.
- Kept uncertain candidate branches in review and capped final confidence by supporting stage evidence; character limits are not exact token guarantees.

## Feature summary

- LM Studio album/track identification with Review and Automatic modes, strict result validation and configurable request settings.
- Native release-type search buttons on artist pages.
- Persistent saved AI decisions for Manual Import previews, with explicit fresh mapping.
- Retry AI is separate from Refresh; unavailable-provider warning spam is suppressed without deleting configuration.
- Complete-release protection and red errors versus yellow review warnings.
- Bounded, staged AI requests and adaptive context/output-limit subdivision.

## Install on Unraid

Download `lidarr-ai-3.2.1.5196-ai.6-amd64.tar.gz`, copy it to your server, and load it:

```sh
docker load -i /path/to/lidarr-ai-3.2.1.5196-ai.6-amd64.tar.gz
```

Set the existing container’s Repository to `lidarr-ai:3.2.1.5196-ai.6`, preserve its mappings/settings, apply, and hard-refresh the browser. Read the included installation instructions before enabling automatic operations.

## Compatibility and historical limitations

- Linux amd64 image, based on upstream Lidarr v3.2.1.5196 and the pinned LinuxServer nightly runtime. Assembly version remains 3.2.1.5196 for plugin compatibility.
- Preserve /config, media mappings, ports, network and database configuration when replacing the container.
- Back up appdata and any external PostgreSQL databases before upgrading; rollback across migrations can require a database restore. Historical releases can lack later fixes and should not be used as a casual downgrade path.
- Test counts describe each original build’s recorded verification, not a fresh rerun during this repository reorganization. Linux container execution and third-party plugin behavior require runtime verification.

## Verification and provenance

86 backend and 19 frontend tests passed.

Original build date: 2026-11-09T20:49:07.371Z. The Docker image and `lidarr-ai-3.2.1.5196-ai.6-build-source.tar.gz` are the original build artifacts; their bytes are checked against the original checksum records. GitHub’s automatic source downloads contain the matching application snapshot plus the reorganized publication documentation. New publication commits/tags replace the former mixed repository history; original build metadata is retained for provenance. No application code was rebuilt or changed during this reorganization.
