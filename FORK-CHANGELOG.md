# Changelog

## 3.2.1.5196-ai.11 — Library Audit and faster library pages

### Added

- Added **Library → Audit** with version-label/duration checks, optional fpcalc/AcoustID evidence, and SHA-256 exact-file duplicate detection.
- Added saved findings, evidence comparisons, type/status filters, pagination, and reversible ignore/restore decisions. Audit scans never modify music files, tags, assignments, or monitoring.
- Added migration 83 for persistent audit findings on SQLite and PostgreSQL. Back up the main database before upgrading; PostgreSQL databases are separate from appdata.
- Included Linux/AMD64 Docker-loadable image, Unraid template, Compose example, source archive, build metadata, and checksums.

### Improved

- Replaced the quadratic album/file-statistics merge with an indexed lookup while preserving results and duplicate-match checks.
- Made web UI artist-list requests use lightweight last/next album summaries instead of nested artist and full release records. Existing API clients keep the full response by default.
- Scoped single-artist album statistics to the requested artist and indexed response statistics/last-next lookups.
- Preserved library selector memoization across renders, corrected collection equality checks, and avoided repeated artist/release navigation sorting on unrelated updates.
- Display artist albums as soon as their data arrives, without waiting for track-file details. File-management controls and error reporting remain available.
- Preserve fpcalc from the pinned LinuxServer base during source Docker builds so optional audio identification remains available.

### Validation and limits

- Production UI build, lint, analyzer-enabled .NET builds, 39 frontend tests, and 90 .NET tests passed.
- The pre-upgrade live artist-list request measured 17.968 seconds initially and 4.075 seconds on repeat, with 26.2 MB JSON. Offline summary projection reduced consistently reserialized JSON by 55.8%; this is not an end-to-end live speedup claim.
- Synthetic 20,000-album merge: 1,440.5 ms before versus 4.1 ms indexed, with identical results. See `docs/UI-PERFORMANCE.md` and `docs/LIBRARY-AUDIT.md`.
- Deep fingerprinting depends on catalog coverage and working fpcalc; uncertain associations remain review evidence. Exact duplicate checks do not match differently tagged or encoded audio.

## 3.2.1.5196-ai.7 — Changes from upstream Lidarr v3.2.1.5196

Based on upstream commit `6af1274a11957270a308fd0448ffe624a8e264d0`. This changelog describes the cumulative changes in the fork relative to that source, rather than the changes between individual fork builds.

### Added

#### Selectable identification engines

- Added **Settings → Identification** to choose between **Original Lidarr** and **LM Studio** matching.
- Retained the original weighted metadata matcher, release comparison, and fingerprint retry path for Original mode. This mode does not call LM Studio.
- Made native minimum match scores configurable from 1–100%, with stock defaults of **80% for albums** and **60% for tracks**. These scores measure weighted metadata similarity, not AI confidence.
- Added settings for the LM Studio API URL, model identifier, Review/Automatic import mode, and minimum AI confidence.
- Preserved Docker environment variables as initial LM Studio defaults. Saved UI settings take precedence for the values exposed in Settings.
- Engine changes apply to subsequent identification and explicit retries; changing the setting alone does not rearm or import held downloads.

#### LM Studio identification

- Added album-aware identification through LM Studio's OpenAI-compatible chat-completions API.
- Supplies filenames, tags, album context, durations, track/disc positions, MusicBrainz identifiers, available fingerprint recording IDs, candidate releases, and existing-file information. Raw audio is not sent.
- Uses structured JSON output and stable file, candidate, and track identifiers. The model must choose from supplied metadata; it cannot invent releases or tracks.
- Validates complete file coverage, identifier uniqueness, selected-release membership, status/track consistency, finite confidence values, explanations, completion status, refusals, and existing-file eligibility before applying results.
- Added **Review** mode, which displays proposals for human approval while blocking automatic AI imports, and **Automatic** mode, which allows validated proposals through the normal import safeguards.
- Keeps failed, invalid, refused, or uncertain identifications held for review. LM Studio failures do not silently fall back to Original matching.
- Added configurable request timeouts, confidence thresholds, request/output limits, reasoning effort, and optional bearer authentication. Model inference is serialized within the application process.
- Added a rotating AI decision audit at `/config/logs/ai-matching.jsonl`, including reasons, identifiers, filenames, and model information without storing API credentials or full prompts.

#### Staged AI requests

- Splits large candidate sets into groups, compares competing proposals, and combines validated results before applying an identification.
- Splits file batches and unusually large track lists as needed while preserving identifiers across stages.
- Defaults to at most **eight files** and a target of **18,000 serialized request characters** per stage. Character limits are a size heuristic, not an exact token count.
- Recognizes context-related HTTP 400 responses and attempts smaller stages. Truncated output can trigger smaller file batches.
- Preserves uncertain candidate branches as review holds rather than silently discarding them. Final comparison confidence cannot exceed the supporting proposal confidence.
- Checks for duplicate track assignments across stages and prevents partial application when a later stage fails.
- Bounds processing to 128 requests and 24 subdivision levels per identification. Indivisible requests that cannot fit remain held with an actionable error.
- Logs stage numbers and request sizes and requests short explanations to reduce excessively verbose model output.

#### Native Unique Singles

- Added **Settings → Unique Singles**, with an independent enable/disable toggle that defaults to **off**.
- Incorporated native duplicate-single cleanup based on the matching approach in [gergc/lidarr-plugin-uniquesingles](https://github.com/gergc/lidarr-plugin-uniquesingles), with MIT attribution retained.
- Requires every track on the single's selected release to have coverage from physically present files on monitored albums or EPs by the same artist.
- Supports automatic cleanup using recording-ID matches and, optionally, normalized title plus duration matches. Default duration tolerance is 3,000 milliseconds.
- Routes title-only matches, conflicting version/duration evidence, and title/duration matches in recording-ID-only mode to manual review.
- Preserves singles with exclusive tracks or B-sides. Excludes Live, Remix, DJ-mix, and Demo releases as comparison sources.
- Requires exact recording-ID coverage before automatically unmonitoring singles without downloaded files.
- Blocks deletion when single files are unmapped, track metadata is incomplete, paths/files are shared with coverage sources, or coverage is incomplete.
- Persists monitoring ownership before cleanup, unmonitors the single, and removes its redundant files through Lidarr's existing deletion and recycle-bin handling. Covering album/EP files are retained and checked again before each deletion.
- Records cleanup failures without automatically repeating failed or interrupted deletion.
- Queues artist-scoped checks after imports and album/file deletions, with a daily scan and an explicit **Scan now** action.
- Re-monitors singles previously unmonitored by this feature when coverage disappears. Alternative downloaded album/EP coverage can keep the single unmonitored.
- Preserves subsequent manual monitoring choices and does not claim ownership of past plugin cleanup decisions.
- Disabling the feature stops scans, automatic cleanup, and automatic monitoring restoration without undoing previous cleanup.

#### Singles Review page

- Added a separate **Activity → Singles Review** page, independent of the download queue.
- Shows per-track comparisons, durations, recording IDs, counterpart albums, matching reasons, and artist/album links.
- Provides status filters, pagination, cleanup status, and scan-command progress.
- Adds **Approve cleanup**, **Keep**, and **Recheck evidence** actions. Approval requires explicit confirmation.
- Revalidates current evidence before approval and rejects stale or incomplete comparisons. Keep preserves the current monitoring state and suppresses future cleanup until explicitly rechecked.
- Prevents pending review decisions from being silently promoted to automatic cleanup by a later scan.
- Persists decisions and monitoring ownership across restarts in the main database, supporting SQLite and PostgreSQL.

#### Native release-type search

- Added search buttons to artist-page release-type headers, including collapsed sections, replacing the need for the inline-search userscript.
- Searches monitored releases belonging to the current artist and section, including completed releases eligible for normal quality upgrades.
- Preserves existing primary/secondary release-type grouping.
- Added command-aware spinners, overlapping-search detection, duplicate-submission guards, and visible retryable errors.
- Uses Lidarr's existing authentication and command APIs.

### Changed

- Queue **Refresh** polls download clients and updates the list without explicitly retrying identification or starting an import-processing worker. Normal background handling of newly completed downloads remains active.
- Added a separate **Retry Matching** button beside Refresh. It retries eligible completed downloads awaiting import using the selected engine, including items hidden by queue filters.
- Retry processing excludes imported, ignored, failed-download, and currently importing items; respects disabled Completed Download Handling; and reports progress through its own command.
- Opening Manual Import in LM Studio mode reads saved proposals without requesting inference. **Map Tracks** remains an explicit fresh identification operation.
- Added persistent AI preview decisions at `/config/ai-import-decisions.json`, with atomic writes and a 5,000-entry limit. File changes and invalid metadata mappings invalidate saved proposals; automatic imports do not reuse this preview cache.
- Opening Manual Import in Original mode performs native identification instead of reusing saved AI proposals.
- Added AI explanations, candidate information, model labels, and saved-result information to manual-import review data.

### Fixed

- Blocked automatic import of new downloads missing expected tracks, including one-file downloads identified as two-track singles. Rejections include missing counts and track titles.
- Retained completeness checks for both identification engines while allowing correctly matched existing library tracks to contribute to release completeness.
- Distinguished definitive errors from uncertainty: true AI non-matches, matching failures, and definite missing-track errors appear **red**; Review-mode proposals and ambiguous identification remain **yellow**.
- Kept Manual Import available for red-error rows and prevented automatic selection of AI review/error proposals.
- Prevented an additional true AI non-match from being hidden by an otherwise complete imported album, while preserving legacy handling of harmless skipped extras.
- Reduced repeated unknown-provider warnings, including `DeemixSettings`, to one actionable warning per provider entry/settings type per process. Messages include recovery information; configuration rows, credentials, and enabled state are preserved.
- Added guards against stale asynchronous UI updates, repeated submissions, and duplicate track assignments.

### Database, packaging, and compatibility

- Added main-database **migration 82** and the `SinglesDecisions` table, with unbounded evidence text, indexed lookup fields, and timezone-aware PostgreSQL timestamps.
- Added a multi-stage source Dockerfile, Linux amd64 image archives, example Compose configuration, an Unraid template, source archives, build reports, and SHA-256 checksums.
- Retained the LinuxServer nightly runtime, s6 startup, `/config` volume, and user/group handling.
- Kept application assembly version `3.2.1.5196` for plugin compatibility; the fork's container/package version is `3.2.1.5196-ai.7`.
- Retained existing plugin interfaces and configuration storage. End-to-end compatibility with individual third-party plugins requires runtime testing.
- Retained the upstream GPL license and included the Unique Singles MIT license attribution.

### Verification

- **121 backend tests passed**, including identification engines, threshold boundaries, AI staging/validation, import protection, saved previews, queue actions, single cleanup, review decisions, and monitoring restoration.
- **26 frontend tests passed**, covering search controls, queue behavior, error severity, settings, review confirmation, stale-evidence handling, filtering, pagination, and command progress.
- Backend compilation with analyzers, modified frontend lint checks, production frontend compilation, and self-contained Linux publishing passed.
- Verified the review migration and repository against isolated SQLite databases and checked generated PostgreSQL schema types.
- Smoke-tested startup, migration, settings persistence/validation, review API access, feature toggling, and an empty-library scan in an isolated local Lidarr instance.
- Validated the Docker archive structure, application-layer digest, image configuration, Unraid template, and delivery checksums.
- Live PostgreSQL migration and end-to-end cleanup on Unraid remain runtime verification steps; the full upstream integration suite was not run.

### Upgrade notes

- Back up the main database and application configuration before upgrading. PostgreSQL data requires a database backup; copying appdata alone does not back it up. Rolling back may require restoring the pre-upgrade database.
- Disable the external UniqueSingles plugin's active cleanup actions before enabling the native feature.
- Disable the old inline-search userscript to avoid duplicate artist-page controls.
- Unique Singles starts disabled. The matching engine defaults to LM Studio to preserve the fork's existing behavior; LM Studio's supplied import-mode default remains Review.
- Metadata-based duplicate matching does not establish bit-identical audio. Re-monitoring permits normal download handling; it does not restore deleted files or force an immediate search.
- PostgreSQL support already existed upstream. This fork adds PostgreSQL-compatible review storage; it does not introduce PostgreSQL support or automatically migrate SQLite libraries.
