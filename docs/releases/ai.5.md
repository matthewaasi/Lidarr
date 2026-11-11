# Lidarr 3.2.1.5196-ai.5 — Complete-release protection and queue severity

## Changes since ai.4

- Blocked automatic import of new downloads missing expected tracks, including a one-file download identified as a two-track single; errors show missing counts and titles.
- Allowed matched existing library tracks to contribute to completeness while preserving library-rescan behavior.
- Marked definitive AI non-matches, inference failures and definite missing-track errors red; review proposals and uncertainty remain yellow.
- Preserved Manual Import for error rows, prevented automatic preselection of error proposals, and kept an extra true non-match from being hidden by a complete album count.

## Feature summary

- LM Studio album/track identification with Review and Automatic modes, strict result validation and configurable request settings.
- Native release-type search buttons on artist pages.
- Persistent saved AI decisions for Manual Import previews, with explicit fresh mapping.
- Retry AI is separate from Refresh; unavailable-provider warning spam is suppressed without deleting configuration.
- Complete-release protection and red errors versus yellow review warnings.

## Install on Unraid

Download `lidarr-ai-3.2.1.5196-ai.5-amd64.tar.gz`, copy it to your server, and load it:

```sh
docker load -i /path/to/lidarr-ai-3.2.1.5196-ai.5-amd64.tar.gz
```

Set the existing container’s Repository to `lidarr-ai:3.2.1.5196-ai.5`, preserve its mappings/settings, apply, and hard-refresh the browser. Read the included installation instructions before enabling automatic operations.

## Compatibility and historical limitations

- Linux amd64 image, based on upstream Lidarr v3.2.1.5196 and the pinned LinuxServer nightly runtime. Assembly version remains 3.2.1.5196 for plugin compatibility.
- Preserve /config, media mappings, ports, network and database configuration when replacing the container.
- Back up appdata and any external PostgreSQL databases before upgrading; rollback across migrations can require a database restore. Historical releases can lack later fixes and should not be used as a casual downgrade path.
- Known historical limitation: staged AI requests were added in ai.6; large prompts in this version can exceed the model context.
- Test counts describe each original build’s recorded verification, not a fresh rerun during this repository reorganization. Linux container execution and third-party plugin behavior require runtime verification.

## Verification and provenance

75 backend and 19 frontend tests passed.

Original build date: 2026-11-09T19:44:49.992Z. The Docker image and `lidarr-ai-3.2.1.5196-ai.5-build-source.tar.gz` are the original build artifacts; their bytes are checked against the original checksum records. GitHub’s automatic source downloads contain the matching application snapshot plus the reorganized publication documentation. New publication commits/tags replace the former mixed repository history; original build metadata is retained for provenance. No application code was rebuilt or changed during this reorganization.
