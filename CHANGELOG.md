# Changelog

Cumulative release history relative to upstream Lidarr v3.2.1.5196. Each entry lists changes introduced in that version.

## 3.2.1.5196-ai.9 — Manual track-picker recovery

- Fixed “No tracks were found for the selected album” when matching has not assigned a release.
- Missing or zero release IDs now use the album’s monitored release for track selection; selecting a track carries its release ID into the final import payload.
- Preserved exact track lists for valid identified releases and retained the requirement for actual album track metadata; no new database migration.

**Recorded verification:** 32 frontend tests passed, including six manual-import regressions; changed-file lint and the production build passed. Backend analyzers remained disabled for this build.

## 3.2.1.5196-ai.8 — Queue AI disposition and refresh settings

- Added the Queue AI Summary column: Approved, Not a match, Issues found or Not assessed.
- Kept Approved as an AI disposition only: Review mode still requires manual import.
- Added Settings → Download Clients → Queue Refresh Interval, configurable from 1–1440 minutes with a one-minute default.
- Updated scheduled polling when the interval is saved, without automatically retrying held AI decisions; no new database migration.

**Recorded verification:** 13 focused backend tests passed; changed-file lint and the production frontend build passed. Backend analyzers were disabled for this build because of existing StyleCop issues.

## 3.2.1.5196-ai.7 — Matching settings and native Unique Singles

- Added Settings → Identification with Original Lidarr or LM Studio engines, configurable native album/track scores (80%/60% defaults), and saved LM Studio settings.
- Restored native matching and fingerprint retry without LM Studio requests; changing engines alone does not retry held downloads. Renamed the explicit queue action to Retry Matching.
- Added optional native Unique Singles cleanup, off by default, based on the gergc fork with MIT attribution. Every single track must have downloaded monitored album/EP coverage.
- Added recording-ID and optional title/duration matching, exclusive-track protection, version-conflict review, file ownership checks and normal recycle-bin deletion.
- Added Activity → Singles Review with per-track evidence, confirmation, approve/keep/recheck actions, stale-evidence protection and persistent monitoring ownership.
- Added automatic re-monitoring when coverage disappears while preserving alternative coverage and manual choices; added main-database migration 82.

**Recorded verification:** 121 backend and 26 frontend tests passed; isolated API/startup checks and PostgreSQL SQL-generation checks passed.

## 3.2.1.5196-ai.6 — Staged AI matching

- Split large candidate sets, file batches and long track lists into bounded requests, then compared proposals before applying a combined decision.
- Preserved global identifiers and rejected duplicate assignments across stages; later-stage failure cannot apply a partial success.
- Added adaptive subdivision for recognized context-limit errors and smaller file batches for truncated output.
- Added configurable defaults of eight files and an 18,000-character request target, short-reason instructions, stage logging and bounded request/depth limits.
- Kept uncertain candidate branches in review and capped final confidence by supporting stage evidence; character limits are not exact token guarantees.

**Recorded verification:** 86 backend and 19 frontend tests passed.

## 3.2.1.5196-ai.5 — Complete-release protection and queue severity

- Blocked automatic import of new downloads missing expected tracks, including a one-file download identified as a two-track single; errors show missing counts and titles.
- Allowed matched existing library tracks to contribute to completeness while preserving library-rescan behavior.
- Marked definitive AI non-matches, inference failures and definite missing-track errors red; review proposals and uncertainty remain yellow.
- Preserved Manual Import for error rows, prevented automatic preselection of error proposals, and kept an extra true non-match from being hidden by a complete album count.

**Recorded verification:** 75 backend and 19 frontend tests passed.

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
