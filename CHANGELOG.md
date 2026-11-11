# Changelog

Cumulative release history relative to upstream Lidarr v3.2.1.5196. Each entry lists changes introduced in that version.

## 3.2.1.5196-ai.2 — Native release-type search

- Added native search buttons to artist-page release-type headers, including collapsed sections.
- Scopes searches to monitored releases belonging to the current artist and section, including completed releases eligible for upgrades.
- Added command-aware spinners, overlapping-search detection, duplicate-click guards and retryable errors; the userscript is no longer needed.

**Recorded verification:** 11 focused frontend tests passed. Backend behavior and its 35-test baseline were retained from ai.1.

## 3.2.1.5196-ai.1 — AI-assisted music identification

- Added album-aware LM Studio matching using filenames, tags, durations, MusicBrainz identifiers, fingerprint candidates and release track lists.
- Added structured JSON responses, supplied-ID validation, confidence checks and fail-closed handling of invalid responses, refusals, timeouts and network errors.
- Added Review and Automatic modes, manual approval support, configurable model/server/request settings and rotating AI decision audit logs.
- Added the Linux amd64 Docker build and Unraid installation assets while retaining the LinuxServer runtime and existing plugin interfaces.

**Recorded verification:** 35 backend tests passed; an initial live Qwen matcher check passed.
