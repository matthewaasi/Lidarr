# Lidarr AI Matcher — 3.2.1.5196-ai.5

Based on upstream `Lidarr/Lidarr` tag `v3.2.1.5196`, commit `6af1274a11957270a308fd0448ffe624a8e264d0`.
The container uses LinuxServer's `nightly-3.2.1.5196-ls224` runtime and s6 startup.

## Behavior

Lidarr still groups files, reads tags, retrieves candidate metadata, and optionally retrieves fingerprint candidates when no tag candidates exist. `IdentificationService` now sends the populated candidate releases and files to `ILmStudioMatcher` instead of its traditional release-scoring algorithm.

Each album batch includes filenames, album context, tags, durations, track/disc numbers, available MusicBrainz IDs, fingerprint recording IDs, candidate track lists, and existing files. LM Studio chooses one release and maps individual files to opaque candidate tokens. The LLM cannot introduce a release or track outside that supplied list. Traditional title-distance gates do not decide acceptance of these AI mappings.

The matcher validates complete file coverage, distinct file and track tokens, selected-release membership, finite confidence in 0..1, explanations, completion status, and existing-file eligibility before applying a batch. Invalid JSON, truncation, refusal, timeout, network/configuration errors, absent candidates, oversized requests and ambiguous release identity require manual review. There is no automatic traditional-matcher fallback.

`Review` mode keeps validated proposals visible in Manual Import but rejects automatic import. Explicitly selecting and importing in Manual Import is a human approval. The UI does not automatically select AI review proposals or true-error rows. `Automatic` mode permits validated mappings above the configured confidence threshold through the normal import safeguards. New downloads must identify every expected track in the selected release; missing or unresolved expected tracks block the entire release's automatic import. Other album safeguards, such as wanted-release and upgrade checks, still apply.

Existing candidate retrieval can still exclude an album or artist. AI does not search the entire MusicBrainz database itself. If no useful candidate appears, narrow or select the artist/album/release in Manual Import and request mapping again. A large batch is rejected for review rather than silently dropping candidates.

## Configuration

Configuration uses container environment variables. Restart/recreate the container to apply changes.

| Variable | Default in supplied container | Meaning |
| --- | --- | --- |
| `LIDARR_AI_BASE_URL` | `http://10.0.0.88:1234/v1` | LM Studio API base, including `/v1` |
| `LIDARR_AI_MODEL` | `qwen/qwen3.8-27b` | Exact model ID from `/v1/models` |
| `LIDARR_AI_MODE` | `Review` | `Review` or `Automatic` |
| `LIDARR_AI_MIN_CONFIDENCE` | `0.95` | Model-estimated certainty threshold; not title similarity |
| `LIDARR_AI_TIMEOUT_SECONDS` | `180` | Total request/queue timeout, 1–1800 seconds |
| `LIDARR_AI_MAX_REQUEST_CHARACTERS` | `180000` | Whole-request size ceiling, 1000–1000000 |
| `LIDARR_AI_API_KEY` | unset | Optional LM Studio bearer token |
| `LIDARR_AI_REASONING_EFFORT` | `none` | Request reasoning setting; `none` avoids lengthy internal reasoning on this Qwen setup |
| `LIDARR_AI_MAX_OUTPUT_TOKENS` | `4096` | Maximum completion length; incomplete responses require review |

Inference calls are serialized within a Lidarr process to avoid simultaneous model requests. Fresh identification uses current metadata and model output. Manual Import previews use saved decisions instead, as described below.

Your Qwen server was observed with an 8192-token loaded context and default `xhigh` reasoning. An initial test with that default timed out at 180 seconds; the same small matching task with `reasoning_effort=none` completed. Increase the loaded context in LM Studio or narrow the candidate batch for larger albums; the character ceiling is not a token/context guarantee.

Manual Import's review popover shows the explanation and candidate releases. The `/api/v1/manualimport` response includes an `aiMatch` object. A rotating JSON-lines audit at `/config/logs/ai-matching.jsonl` records the filenames, decision reasons, candidate IDs, and model. One 10 MiB backup is retained. Raw audio is not sent. Credentials and full prompts are not written to that audit.

## Build from source

On an x86-64 Docker host, from this source directory:

```sh
docker build --platform linux/amd64 -t lidarr-ai:3.2.1.5196-ai.5 .
```

The multi-stage Dockerfile builds the UI with Node 22/Yarn 1.22.22 and publishes the application plus the dynamically loaded `Lidarr.Mono` platform library with .NET SDK 8.0.421 for `linux-musl-x64`.

Plugin APIs and plugin storage are retained. Tidal and UniqueSingles remain separate installations under the copied `/config` directory. Their end-to-end behavior must be verified on the running Unraid instance; preservation of interfaces and files alone is not a runtime compatibility guarantee. Existing Docker mods or extra runtime dependencies must also be carried over from the original container configuration.

The public legacy `MapReleaseTracks` helper is retained for binary/source compatibility, but the built-in identification pipeline no longer calls it. Explicit human track selections in Manual Import remain supported.

This is a local fork under the upstream GPL-3.0 license. No GitHub fork or public registry image has been published.

## Native release-type search (ai.2)

The artist page has a search button on every existing release-type header, including collapsed sections. It queues Lidarr's standard `AlbumSearch` command for the monitored releases listed in that artist's section. Completed releases remain eligible so normal quality-upgrade rules can apply. Unmonitored releases and releases belonging to other artists or sections are excluded.

The button displays a spinner and is disabled while its request is being submitted or an overlapping album/artist search is queued or running. A failed submission shows a retryable error beside the section. Running/completed/failed search progress uses Lidarr's normal command notifications. The native authentication and API URL handling are used, with no TamperMonkey extension or separately stored API key.

Live and Compilation are secondary metadata classifications and remain within the existing primary-type groups; this feature does not introduce overlapping new groups. It also works for Broadcast, Other, and any other existing group rather than hardcoding a list of headings. Disable the old TamperMonkey script when using this build to avoid its additional injected buttons.

Focused UI tests can be run after installing the frontend dependencies:

```sh
node --test frontend/test/releaseTypeSearch.test.cjs
```

## Queue actions and saved Manual Import previews (ai.4)

**Refresh** on Activity → Queue now sends `RefreshMonitoredDownloads` with `queueOnly=true`: it polls download clients and updates the list without checking imports or queueing an import-processing worker. Normal background handling still processes newly completed downloads independently.

The separate **Retry AI** button beside Refresh sends `RetryAiMatches`. It refreshes the tracked downloads, rearms eligible completed import failures (including Review holds and prior LM Studio failures), and runs the normal matching/import pipeline for completed downloads awaiting import. It covers the queue across filters, not just selected rows. Imported, ignored, failed-download, and currently importing items are not rearmed. Background/scheduled polling does not force retries of held imports. Completed Download Handling must be enabled. In Automatic mode a successful retry can proceed to import under the usual safeguards; Review mode still holds the result for approval. The button's spinner follows the dedicated command through processing; duplicate clicks and duplicate active commands are guarded.

Opening **Manual Import** for a folder or download only reads the latest saved per-file results. It never falls back to inference if a result is missing. **Map Tracks** remains an explicit fresh identification operation. Existing POST-based mapping edits retain their fresh-identification behavior; opening/browsing the dialog is the operation changed here.

Results and original rejection reasons are saved atomically to `/config/ai-import-decisions.json`, retaining up to 5000 recent file entries. A size/mtime change or missing/cross-release metadata invalidates a saved proposal. Preview never uses saved mappings for automatic importing. If no valid saved result exists, use Retry AI or Map Tracks. The review popover identifies saved results. This cache persists across restarts and does not store API credentials or raw audio. Entries from builds before ai.3 are not reconstructed from audit logs; use Retry AI once to create saved results for held downloads if needed.

## Unavailable provider warnings (ai.4)

`DownloadClientRepository` reads a persisted download-client row whose `ConfigContract` names the provider settings class. The warning about `DeemixSettings` means that class is unavailable in the loaded assemblies (for example, an old Deemix configuration whose plugin is no longer installed or failed to load). The provider was already being skipped; this is independent of the AI matcher.

The repository now logs one actionable warning per unavailable entry/settings type per process instead of on every poll. It includes the name and ID and preserves the database row, enable state, settings, and credentials. A returning provider clears the suppression for that same entry. Other provider errors are not suppressed. This avoids deleting a potentially recoverable plugin configuration. Restoring the matching provider/plugin, or removing a verified obsolete database entry while Lidarr is stopped and backed up, resolves the underlying configuration issue completely. The supplied diagnostic SQL only lists the Deemix entry and does not expose its settings or modify it.

## Completeness and queue severity (ai.5)

The AI exception in the old completeness specification was too broad. ai.5 restores completeness enforcement for new downloads before that exception. One matched file for a two-track release is rejected with a missing count and track titles, even if the AI is confident and Automatic mode is enabled. Existing correctly matched library files included in the selected candidate can contribute to completeness, so upgrades can still use tracks already present. Library scans are not treated as new full-release downloads. No previously imported file is removed or repaired automatically.

If enough files exist but some expected identities are uncertain, the release is held for review rather than calling uncertainty a definitive non-match. Explicit `no_match`, inference/configuration failures, request-size failures, and definite missing-track shortfalls are true errors. They set the queue status to Error (red). Review mode alone, low confidence, and ambiguous identity remain Warning (yellow). Failed import operations are also marked red. Manual Import remains available for both colors; error rows are not automatically selected.

AI results now carry a status and IsError flag through saved previews. Use Retry AI after upgrading to reclassify old held results. Model request/response schema, current model default, and endpoint are unchanged. Changing a compatible model only requires loading it in LM Studio and updating LIDARR_AI_MODEL to the exact server model ID in the Docker configuration.
