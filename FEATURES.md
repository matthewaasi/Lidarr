# Features — 3.2.1.5196-ai.17

- Search-scoped matching and album-state caches, bounded workers, stable ordered progress, full interactive rejection details, and processing throughput/stage timings.
- Live Library Audit phase percentages and counters; running task cancellation and named search progress remain available.

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
- Read-only Library Audit, persistent audit review, optional fingerprint evidence, exact-file duplicate checks, and library/artist page performance improvements.

## Compatibility

- Linux amd64 image, based on upstream Lidarr v3.2.1.5196 and the pinned LinuxServer nightly runtime. Assembly version remains 3.2.1.5196 for plugin compatibility.
- Preserve /config, media mappings, ports, network and database configuration when replacing the container.
- This version includes migration 82 for Singles Review. Unique Singles starts off; disable the external plugin’s cleanup actions before enabling the native feature.
- Migration 83 adds Library Audit findings. Library Audit reports evidence without changing music or monitoring.
- Back up appdata and any external PostgreSQL databases before upgrading; rollback across migrations can require a database restore. Historical releases can lack later fixes and should not be used as a casual downgrade path.
- Test counts describe each original build’s recorded verification, not a fresh rerun during this repository reorganization. Linux container execution and third-party plugin behavior require runtime verification.
