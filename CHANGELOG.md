# Changelog

Cumulative release history relative to upstream Lidarr v3.2.1.5196. Each entry lists changes introduced in that version.

## 3.2.1.5196-ai.4 — Dedicated AI retry and quieter provider warnings

- Added Retry AI beside Queue Refresh with independent command progress and duplicate-submission guards.
- Restored Refresh to polling and list updates without requesting identification or starting an import-processing worker.
- Kept Manual Import saved-result-only on opening; explicit mapping still performs identification.
- Reduced unavailable-provider warnings, including DeemixSettings, to one actionable message per entry/settings type per process while retaining configuration and credentials.

**Recorded verification:** 57 backend and 15 frontend tests passed.

## 3.2.1.5196-ai.3 — Saved AI previews and explicit retry

- Opening Manual Import now reads saved AI proposals without making an inference request; explicit Map Tracks still requests fresh identification.
- Added atomic persistent preview caching with file size/mtime and metadata validation; automatic imports do not reuse preview decisions.
- In this release only, Queue Refresh explicitly retries completed held downloads. This temporary control behavior is replaced by a dedicated button in ai.4.
- Kept scheduled polling from forcing retries of held identifications and excluded imported, ignored, failed-download and active-import items.

**Recorded verification:** 52 backend and 12 frontend tests passed.

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
