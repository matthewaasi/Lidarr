# Lidarr 3.2.1.5196-ai.11 — Library Audit and faster library pages

## Changes since ai.10

- Added Library → Audit for read-only review of version-label and duration mismatches, optional fingerprint evidence, and exact duplicate files.
- Exact duplicate checks group by file size and compare complete SHA-256 file hashes; optional deep checks use fpcalc/AcoustID rather than uploading music files.
- Added persistent findings, paginated filters, evidence-aware ignore/restore actions and partial-scan handling; the audit does not delete, retag, remap, change monitoring or download music.
- Added main-database migration 83 for persistent audit findings.
- Indexed album/statistics lookups, added lightweight artist summaries for the web UI, preserved full API responses by default, and scoped album statistics to the requested artist.
- Preserved memoized library selectors, cached navigation sorting, and displayed artist albums before track-file loading finishes. Local benchmarks are not a measured production speedup.

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
- Read-only Library Audit, persistent audit review, optional fingerprint evidence, exact-file duplicate checks, and library/artist page performance improvements.

## Install on Unraid

Download `lidarr-ai-3.2.1.5196-ai.11-amd64.tar.gz`, copy it to your server, and load it:

```sh
docker load -i /path/to/lidarr-ai-3.2.1.5196-ai.11-amd64.tar.gz
```

Set the existing container’s Repository to `lidarr-ai:3.2.1.5196-ai.11`, preserve its mappings/settings, apply, and hard-refresh the browser. Read the included installation instructions before enabling automatic operations.

## Compatibility and historical limitations

- Linux amd64 image, based on upstream Lidarr v3.2.1.5196 and the pinned LinuxServer nightly runtime. Assembly version remains 3.2.1.5196 for plugin compatibility.
- Preserve /config, media mappings, ports, network and database configuration when replacing the container.
- This version includes migration 82 for Singles Review. Unique Singles starts off; disable the external plugin’s cleanup actions before enabling the native feature.
- Migration 83 adds Library Audit findings. Library Audit reports evidence without changing music or monitoring.
- Back up appdata and any external PostgreSQL databases before upgrading; rollback across migrations can require a database restore. Historical releases can lack later fixes and should not be used as a casual downgrade path.
- Test counts describe each original build’s recorded verification, not a fresh rerun during this repository reorganization. Linux container execution and third-party plugin behavior require runtime verification.

## Verification and provenance

129 tests passed: 84 core, 6 API and 39 frontend. Analyzers, clean production frontend build, isolated startup/migration/API checks and Docker archive validation passed.

Original build date: 2026-11-10T19:36:54.269823Z. The Docker image and `lidarr-ai-3.2.1.5196-ai.11-build-source.tar.gz` are the original build artifacts; their bytes are checked against the original checksum records. GitHub’s automatic source downloads contain the matching application snapshot plus the reorganized publication documentation. New publication commits/tags replace the former mixed repository history; original build metadata is retained for provenance. No application code was rebuilt or changed during this reorganization.
