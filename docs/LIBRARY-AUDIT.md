# Library Audit

Open **Library → Audit** (`/library/audit`) and choose **Scan library**.

The audit reads existing managed and unmapped track files and stores review findings. Scans never change music files, track assignments, tags, monitoring, or downloads. Optional file deletion is a separate, explicit action. Use each finding's album/unmapped-files link to review assignments with Lidarr's existing tools.

## Scan types

- **Version audit:** compares the filename, embedded title, assigned catalog title, and actual audio duration. Recognizes explicit extended, radio edit/mix/version, and original mix/version suffixes. Missing extended labels and conflicting labels are reported as uncertain. Differences over 10 seconds from the assigned track can flag a version problem; same-artist catalog alternatives within 3 seconds are shown as candidates. Multi-track files are excluded from single-track version comparisons.
- **Exact duplicates:** groups files by their actual size, then hashes complete equal-sized files with SHA-256. Only identical file bytes are reported as exact duplicates. Copies with different tags, codecs, or encodings will not match. Hard links may be reported as identical files; this is not an estimate of recoverable storage.
- **Deep version check (optional):** uses existing fpcalc/AcoustID support, with a lookup score threshold of 0.9. Requires Version audit and working fpcalc. Sends fingerprints and durations, not music files, to AcoustID. Checks assigned single-track files, so large libraries can take considerable time. Catalog candidates are restricted to the file's artist. Associations with different version labels can support a mismatch; associations that also include the assigned recording are ambiguous. No result or failed lookup is reported as unavailable evidence, not proof that a file is correct.

The audit cannot conclusively establish every mix label. Duration, title conventions, catalog coverage, and fingerprint associations all have limitations. It does not compare full audio structure or identify near-duplicate audio across encodings.

## Review

Findings include paths, available titles, actual/catalog duration, recording IDs, candidates, confidence, and suggested next steps. Filter by type and review status. Lists are paginated.

**Ignore finding** previews the decision before saving. Ignores persist while evidence is unchanged. A later scan creates a fresh finding if relevant metadata, file size, modification time, or other evidence changes. **Return to review** reverses an ignore. Automatic retagging is not provided.

A successful scan marks findings that are no longer detected as resolved for the selected scan types only. Read failures preserve prior evidence for affected files. Local-only scans do not resolve older fingerprint-supported findings. Refreshing a browser does not cancel a server-side command; progress remains available under System → Tasks.

## API

- `POST /api/v1/command` with `{"name":"ScanLibraryAudit","versions":true,"duplicates":true,"fingerprints":false}` starts a background scan. Poll the returned command ID using the standard command API.
- `GET /api/v1/libraryaudit?status=pending&kind=all&page=1&pageSize=25` lists findings. Status: `pending`, `ignored`, `resolved`, `all`. Kind: `version`, `duplicate`, `errors`, `all`. Page size is limited to 100.
- `POST /api/v1/libraryaudit/{id}` with `{"action":"ignore","evidenceKey":"<current key>"}` ignores a finding. Use `restore` to return it to review. A stale evidence key or resolved finding is rejected.

Existing authentication and URL-base handling apply. Migration 83 adds `LibraryAuditFindings` to the main database using SQLite/PostgreSQL-compatible schema. No new packages are required.

## Validation

Tests under `MediaFiles/LibraryAudit` cover version detection in both directions, ambiguous fingerprint associations, identical bytes versus equal size, file changes during inspection, unavailable evidence, persistence, ignore/restore, stale decisions, partial scans, and SQLite/PostgreSQL migration behavior.

Build with the repository's normal .NET and frontend build processes. Include `SolutionDir` pointing at `src/` when directly building a project so the existing StyleCop configuration is loaded.


## Deletion in ai.13

Delete an individual finding, select multiple findings on the current page, or delete all findings. **Delete all findings removes saved audit records across every status and page; it never removes your library or music files.** Deleting a finding does not suppress future scans; use Ignore for unchanged evidence you want to keep out of Needs review.

Individual and selected finding confirmations offer optional per-file checkboxes, initially unchecked. Choose exactly which copies to remove and select **Preview file deletion**. Review the exact paths, file count and total file size before confirming. Recycled files and hard links mean the total is not a guarantee of immediately recoverable disk space.

The preview warns when every remaining audited copy in a duplicate group would be removed. A separate acknowledgement is required to proceed. The server checks pending and ignored duplicate findings outside the current view too, binds confirmation to the reviewed selection, and rejects stale previews. Changing the selection clears the preview and acknowledgement. Resolved findings and changed or moved files cannot authorize file removal.

File removal follows Lidarr's recycle-bin settings and removes selected file records. Artist and album entries remain; monitoring is unchanged, so missing tracks can be downloaded again. On a partial failure, the batch stops and findings are retained. Some selected files may already be deleted; refresh and rescan before retrying.

API: `DELETE /api/v1/libraryaudit/{id}`, `DELETE /api/v1/libraryaudit/bulk` (JSON ID array), and `DELETE /api/v1/libraryaudit/all` remove findings only. `POST /api/v1/libraryaudit/deletefiles/preview` accepts `{ "findings": [{ "id": 1, "evidenceKey": "..." }], "fileIds": [123] }`. The final `POST /api/v1/libraryaudit/deletefiles` additionally requires the returned `previewKey` and, when every copy is selected, `confirmAllCopies: true`.


## Live progress in ai.16

The Audit page displays a native progress bar with the current phase, stage number, completed/total counts and percentage. Inspection counts files attempted, including inspection errors; duplicate checking counts candidate files hashed or checked. Counts update in memory for each file, with log messages throttled to avoid one extra message per file. Fingerprint lookups and duplicate-group verification are named in the current activity.

Percentages apply to the current phase, not elapsed time or a weighted estimate of the whole scan. They can reset when the phase changes. Loading and final saving are indeterminate because their total work is not known in advance. The page marks the audit completed only after the command completes; cancelled or failed scans retain their last phase progress and are labelled stopped.

Progress is supplied in `command.body.progress` through the existing command API. The page polls every two seconds, reconnects to a running scan after reopening, and retries failed progress requests while labelling the displayed values as last received. A new scan clears previous progress. Closing the page does not cancel the server scan. Progress is transient and cannot be retrofitted to a scan running on an older version; upgrading requires a restart and progress is available for new scans afterwards.

This feature does not alter findings, matching thresholds, cancellation safety or scan eligibility. Runtime progress is excluded from command equality, so updating a counter does not break duplicate-command detection.
