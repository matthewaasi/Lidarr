# Changelog

Cumulative release history relative to upstream Lidarr v3.2.1.5196. Each entry lists changes introduced in that version.

## 3.2.1.5196-ai.21

- Add Retry Automatic Import to the Unmapped Files toolbar to rerun matching against artists already in the library.

## 3.2.1.5196-ai.20

- Add separate Automatic identification method and Manual identification method settings.
- Manual Import defaults to Same as automatic. Its preview never sends a fresh request to LM Studio.
- Map Tracks uses the manual method. Retry Matching uses the automatic method.

## 3.2.1.5196-ai.19 — Album-wide AI reconciliation

- File batches that select different editions or leave release identity uncertain trigger one album-wide AI selection using all original files and candidates.
- Remap all files against the complete selected release; discard earlier conflicting mappings and validate the final mapping globally.
- A no-match-only batch no longer erases the release identified by other batches. Individually uncertain files remain review items after independent album selection.
- Preserve Review mode, confidence thresholds, valid candidate/track IDs, request limits and atomic failure handling. Uncertain editions, context/output limits and invalid mappings still require review.
- Existing import reviews need a fresh Retry Matching attempt after upgrading. No schema migration is introduced; ai.18 search-performance changes remain included.

See [ai.19 release notes](docs/releases/ai.19.md).

## 3.2.1.5196-ai.18 — Batched logging and indexed matching

- Batch debug/trace file writes through bounded asynchronous queues while retaining cleansing, rotation and graceful flushing. Info logging stays synchronous.
- Read and index the artist catalog once per search; index exact album lookup by artist. Preserve matching thresholds, candidate coverage, ambiguity and year rules.
- Cache normalized artist aliases and reuse criteria regexes within each search. Find unique fuzzy matches without sorting the entire artist catalog.
- Emit periodic and stopped-search throughput/stage summaries, rather than logging timings only on completion.
- Compare production parsing on 5,168 privately captured titles and original/indexed artist scoring on 2,419 captured strings plus fixture examples. The library catalog in the replay is synthetic; production accuracy and 500 releases/sec remain deployment validation targets.

See [ai.18 release notes](docs/releases/ai.18.md) and [performance details](docs/SEARCH-PERFORMANCE.md).

## 3.2.1.5196-ai.17 — Faster release evaluation

- Cache exact/fuzzy artist and album matches, candidate albums, album files, missing tracks and history within each search, including unsuccessful matches.
- Evaluate bounded windows with configurable workers (`LIDARR_RELEASE_WORKERS`, 1–16; default CPU count capped at four), returning decisions in input order and retaining cooperative cancellation.
- Materialize shared lazy entities once across workers. Keep caches isolated between searches and bypass them outside release evaluation.
- Automatic searches evaluate cheap quality/requested-album/title gates first within their original priority group and stop after a permanent rejection. Interactive searches preserve all rejection reasons and their order. Temporary-only rejections remain distinct from permanent rejections.
- Log elapsed processing time, throughput, worker count, cache hits/misses and cumulative stage timings.
- No database migration. Production 500 releases/sec remains a deployment benchmark target, not a measured claim.

See [ai.17 release notes](docs/releases/ai.17.md) for verification and installation.

## 3.2.1.5196-ai.12 — Queue refresh interval in seconds

- Settings → Download Clients → Queue now accepts a whole number of seconds from 1–86,400; default 60 seconds.
- Existing minute-based values are converted on read unless a seconds setting has been saved (5 minutes becomes 300 seconds).
- Saving updates the live schedule. A one-second scheduler tick supports sub-minute intervals; execution depends on task completion and queue availability.
- Other scheduled task intervals retain minute units. System → Tasks reports the queue's actual next execution and fractional-minute API interval.
- The download-client configuration API uses `queueRefreshIntervalSeconds`. No database schema migration is required.

**Recorded verification:** 70 tests passed (20 core, 10 API, 40 frontend). Production UI and Linux publishing passed. Corrected the initial local archive’s duplicate fpcalc entry and verified unique paths in all image layers. Docker loading and Linux container startup require deployment-host verification.

## 3.2.1.5196-ai.11 — Library Audit and faster library pages

- Added Library → Audit for read-only review of version-label and duration mismatches, optional fingerprint evidence, and exact duplicate files.
- Exact duplicate checks group by file size and compare complete SHA-256 file hashes; optional deep checks use fpcalc/AcoustID rather than uploading music files.
- Added persistent findings, paginated filters, evidence-aware ignore/restore actions and partial-scan handling; the audit does not delete, retag, remap, change monitoring or download music.
- Added main-database migration 83 for persistent audit findings.
- Indexed album/statistics lookups, added lightweight artist summaries for the web UI, preserved full API responses by default, and scoped album statistics to the requested artist.
- Preserved memoized library selectors, cached navigation sorting, and displayed artist albums before track-file loading finishes. Local benchmarks are not a measured production speedup.

**Recorded verification:** 129 tests passed: 84 core, 6 API and 39 frontend. Analyzers, clean production frontend build, isolated startup/migration/API checks and Docker archive validation passed.

## 3.2.1.5196-ai.10 — Native-first identification with AI fallback

- Added the selectable Lidarr first, LM Studio fallback engine while preserving existing engine selections until changed in Settings.
- Runs native matching and allowed fingerprinting first; successful identification and new-download completeness require no AI request.
- Sends a failed identification’s whole release to LM Studio under its existing confidence and Review/Automatic settings.
- Keeps quality, duplicate and path/import failures from triggering AI fallback; an AI error never restores a weak native result.
- Manual preview tries native matching and reuses saved AI results without calling the model. Explicit mapping and Retry Matching perform fresh hybrid identification; no new migration.

**Recorded verification:** 87 backend and 33 frontend tests passed. Production frontend and Linux publishing passed; backend analyzers were disabled for this build.

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
