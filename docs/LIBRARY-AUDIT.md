# Library Audit

Open **Library → Audit** (`/library/audit`) and choose **Scan library**.

The audit reads existing managed and unmapped track files and stores review findings. It never changes music files, track assignments, tags, monitoring, or downloads. Use each finding's album/unmapped-files link to review assignments with Lidarr's existing tools.

## Scan types

- **Version audit:** compares the filename, embedded title, assigned catalog title, and actual audio duration. Recognizes explicit extended, radio edit/mix/version, and original mix/version suffixes. Missing extended labels and conflicting labels are reported as uncertain. Differences over 10 seconds from the assigned track can flag a version problem; same-artist catalog alternatives within 3 seconds are shown as candidates. Multi-track files are excluded from single-track version comparisons.
- **Exact duplicates:** groups files by their actual size, then hashes complete equal-sized files with SHA-256. Only identical file bytes are reported as exact duplicates. Copies with different tags, codecs, or encodings will not match. Hard links may be reported as identical files; this is not an estimate of recoverable storage.
- **Deep version check (optional):** uses existing fpcalc/AcoustID support, with a lookup score threshold of 0.9. Requires Version audit and working fpcalc. Sends fingerprints and durations, not music files, to AcoustID. Checks assigned single-track files, so large libraries can take considerable time. Catalog candidates are restricted to the file's artist. Associations with different version labels can support a mismatch; associations that also include the assigned recording are ambiguous. No result or failed lookup is reported as unavailable evidence, not proof that a file is correct.

The audit cannot conclusively establish every mix label. Duration, title conventions, catalog coverage, and fingerprint associations all have limitations. It does not compare full audio structure or identify near-duplicate audio across encodings.

## Review

Findings include paths, available titles, actual/catalog duration, recording IDs, candidates, confidence, and suggested next steps. Filter by type and review status. Lists are paginated.

**Ignore finding** previews the decision before saving. Ignores persist while evidence is unchanged. A later scan creates a fresh finding if relevant metadata, file size, modification time, or other evidence changes. **Return to review** reverses an ignore. No bulk deletion or automatic retagging is provided.

A successful scan marks findings that are no longer detected as resolved for the selected scan types only. Read failures preserve prior evidence for affected files. Local-only scans do not resolve older fingerprint-supported findings. Refreshing a browser does not cancel a server-side command; progress remains available under System → Tasks.

## API

- `POST /api/v1/command` with `{"name":"ScanLibraryAudit","versions":true,"duplicates":true,"fingerprints":false}` starts a background scan. Poll the returned command ID using the standard command API.
- `GET /api/v1/libraryaudit?status=pending&kind=all&page=1&pageSize=25` lists findings. Status: `pending`, `ignored`, `resolved`, `all`. Kind: `version`, `duplicate`, `errors`, `all`. Page size is limited to 100.
- `POST /api/v1/libraryaudit/{id}` with `{"action":"ignore","evidenceKey":"<current key>"}` ignores a finding. Use `restore` to return it to review. A stale evidence key or resolved finding is rejected.

Existing authentication and URL-base handling apply. Migration 83 adds `LibraryAuditFindings` to the main database using SQLite/PostgreSQL-compatible schema. No new packages are required.

## Validation

Tests under `MediaFiles/LibraryAudit` cover version detection in both directions, ambiguous fingerprint associations, identical bytes versus equal size, file changes during inspection, unavailable evidence, persistence, ignore/restore, stale decisions, partial scans, and SQLite/PostgreSQL migration behavior.

Build with the repository's normal .NET and frontend build processes. Include `SolutionDir` pointing at `src/` when directly building a project so the existing StyleCop configuration is loaded.
