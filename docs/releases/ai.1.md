# Lidarr 3.2.1.5196-ai.1 — AI-assisted music identification

## Changes from upstream v3.2.1.5196

- Added album-aware LM Studio matching using filenames, tags, durations, MusicBrainz identifiers, fingerprint candidates and release track lists.
- Added structured JSON responses, supplied-ID validation, confidence checks and fail-closed handling of invalid responses, refusals, timeouts and network errors.
- Added Review and Automatic modes, manual approval support, configurable model/server/request settings and rotating AI decision audit logs.
- Added the Linux amd64 Docker build and Unraid installation assets while retaining the LinuxServer runtime and existing plugin interfaces.

## Feature summary

- LM Studio album/track identification with Review and Automatic modes, strict result validation and configurable request settings.

## Install on Unraid

Download `lidarr-ai-3.2.1.5196-ai.1-amd64.tar.gz`, copy it to your server, and load it:

```sh
docker load -i /path/to/lidarr-ai-3.2.1.5196-ai.1-amd64.tar.gz
```

Set the existing container’s Repository to `lidarr-ai:3.2.1.5196-ai.1`, preserve its mappings/settings, apply, and hard-refresh the browser. Read the included installation instructions before enabling automatic operations.

## Compatibility and historical limitations

- Linux amd64 image, based on upstream Lidarr v3.2.1.5196 and the pinned LinuxServer nightly runtime. Assembly version remains 3.2.1.5196 for plugin compatibility.
- Preserve /config, media mappings, ports, network and database configuration when replacing the container.
- Back up appdata and any external PostgreSQL databases before upgrading; rollback across migrations can require a database restore. Historical releases can lack later fixes and should not be used as a casual downgrade path.
- Known historical limitation: the strict missing-track safeguard and red/yellow AI error classification were added in ai.5.
- Known historical limitation: staged AI requests were added in ai.6; large prompts in this version can exceed the model context.
- Test counts describe each original build’s recorded verification, not a fresh rerun during this repository reorganization. Linux container execution and third-party plugin behavior require runtime verification.

## Verification and provenance

35 backend tests passed; an initial live Qwen matcher check passed.

Original build date: 2026-11-09T18:03:21.567Z. The Docker image and `lidarr-ai-3.2.1.5196-ai.1-build-source.tar.gz` are the original build artifacts; their bytes are checked against the original checksum records. GitHub’s automatic source downloads contain the matching application snapshot plus the reorganized publication documentation. New publication commits/tags replace the former mixed repository history; original build metadata is retained for provenance. No application code was rebuilt or changed during this reorganization.
