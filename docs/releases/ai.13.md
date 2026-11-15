# Lidarr 3.2.1.5196-ai.13 — Audit deletion and file previews

Library → Audit now supports deleting one finding, selected findings on the current page, or all saved findings across every status and page. **Delete all findings clears audit records only. It does not delete the library or music files.** Scans may rediscover deleted findings; Ignore remains available for persistent suppression of unchanged evidence.

Individual and selected finding confirmations offer optional file checkboxes. No file is selected by default. Select the exact copies to remove, then review a deletion preview showing paths, file count and total file size. Removing every remaining audited copy in a duplicate group requires an additional explicit acknowledgement. Duplicate protection includes findings outside the current page and filters. The server validates the evidence and rejects missing or stale previews.

File removal uses Lidarr's existing deletion service and recycle-bin settings. It removes selected file records, retains artist and album entries, and leaves monitoring unchanged; missing tracks may be downloaded again. Recycled files and hard links mean total file size is not a guarantee of immediately freed space. If deletion fails partway through, findings are retained and the page reports that some files may already have been removed. Refresh and rescan before retrying.

## Installation

1. Back up appdata and any external PostgreSQL database.
2. Load the image: `docker load -i lidarr-ai-3.2.1.5196-ai.13-amd64.tar.gz`.
3. Set the container image to `lidarr-ai:3.2.1.5196-ai.13`, preserving existing volumes, ports, network and database settings.
4. Recreate/start the container and hard-refresh the browser.
5. Test finding-only deletion and preview behavior first. Verify file deletion and recycle-bin recovery with disposable music files before using it on your library.

No database migration is added by ai.13. The assembly version remains 3.2.1.5196 for plugin compatibility. This release includes the prior seconds-based queue refresh setting and matching features.

## API

`DELETE /api/v1/libraryaudit/{id}`, `DELETE /api/v1/libraryaudit/bulk` (JSON finding ID array) and `DELETE /api/v1/libraryaudit/all` remove only audit findings. `POST /api/v1/libraryaudit/deletefiles/preview` validates selected finding evidence keys and file IDs, returning exact files, total bytes, affected duplicate groups and a preview key. The final `POST /api/v1/libraryaudit/deletefiles` requires that preview key and a separate `confirmAllCopies` flag for selections removing every audited copy.

## Build and verification

The archive contains freshly compiled Linux musl x64 application binaries and a production UI on the same pinned LinuxServer base as ai.12. Existing /config is not packaged or modified. The application directory is replaced with an opaque OCI whiteout, preserving the base image's fpcalc exactly once.

Executed checks and limitations are recorded in the attached verification report. No Docker daemon or Linux runtime is installed on the build host, so Docker load, container startup, live browser behavior and third-party plugins require deployment-host verification. The Docker-loadable archive is attached to this release; no container registry image is implied.

To build on a Docker host: `docker build --platform linux/amd64 --build-arg VCS_REF="$(git rev-parse HEAD)" -t lidarr-ai:3.2.1.5196-ai.13 .`.

## Recorded checks

99 tests passed: 50 core audit/version tests and 49 frontend tests. The normal VSTest runner completed successfully. Analyzer-enabled .NET builds used the repository's `SolutionDir` setting so StyleCop loaded its configuration. Production UI build, Linux Console/Mono publish, changed-frontend lint and diff checks passed. Container archive digest, path, executable and compiled-file checks are recorded in `verification.json`.
