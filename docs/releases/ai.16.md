# Lidarr 3.2.1.5196-ai.16 — Live Audit-page progress

## Audit percentage indicator

Library → Audit now displays a progress bar with the current stage and phase, percentage, completed/total file counts, current activity and last progress-update time. For example: **Stage 1 of 3 · Inspecting files — 32.5%**, with **74,366 / 228,816 files inspected**.

Inspection counts completed file attempts, including attempts that produce inspection errors. Duplicate checking counts candidate files hashed or checked and identifies current duplicate-group verification. Optional fingerprint lookups are shown as the current activity. Percentages apply to the current phase, not time remaining or a weighted estimate of the entire scan. They may reset when the phase changes. Loading and final saving remain indeterminate where a meaningful total is unavailable. The page marks the audit complete only after the command finishes.

The page polls progress every two seconds and reconnects to an active scan if closed and reopened. A temporary polling failure keeps the last received progress visible with a warning and retries. Cancelled or failed scans retain their last phase progress and are labelled stopped; they never claim successful completion. A new scan clears the previous indicator.

Progress updates do not change matching, findings, scan eligibility, cancellation safety or duplicate-command detection. Log messages are throttled to avoid adding a log entry for every file. The page uses existing command API authentication and URL-base handling. No database migration or new package is required.

## Installation

1. Back up appdata and any external PostgreSQL database.
2. Load the image: `docker load -i lidarr-ai-3.2.1.5196-ai.16-amd64.tar.gz`.
3. Set container `lidarr-ai` to image `lidarr-ai:3.2.1.5196-ai.16`, preserving existing volumes, ports, network and database settings.
4. Recreate/start the container and hard-refresh the browser.
5. Start a new audit under Library → Audit. Confirm that its phase, percentage and counters update, then close/reopen the page to verify reconnection.

**The progress indicator cannot be retrofitted to a scan already running on ai.13 or another older version.** Upgrading restarts Lidarr and interrupts running tasks; new scans after upgrading supply progress. Closing the Audit page itself does not cancel its server-side scan. Running-audit cancellation remains available under System → Tasks until final saving begins.

Assembly version remains 3.2.1.5196 for plugin compatibility. Prior interactive-search progress, task cancellation, matching and audit file-management features remain available.

## API

The existing command resource exposes audit progress at `body.progress`: `phase`, `processed`, `total`, `percentage`, `stage`, `stages`, `activity` and `updated`. Unknown totals and percentages are omitted/null. Progress is runtime state; restarting the application does not resume the interrupted scan or its counters. Command equality ignores runtime progress so repeated scan requests still detect an identical queued/running command.

See [Library Audit details](https://github.com/matthewaasi/Lidarr/blob/v3.2.1.5196-ai.16/docs/LIBRARY-AUDIT.md).

## Verification and limits

144 tests passed: 70 frontend, 66 core and 8 API. New checks cover completed file counts and percentages, inspection errors, distinct duplicate/saving phases, cancellation and failed saves without false completion, empty libraries, command deduplication, structured API serialization, reopening an active scan, polling interruption/recovery and failed scan submission. Existing cancellation and 15,000-report synthetic matching parity checks remain included. Production UI build, analyzer-enabled backend builds, changed-file lint and diff checks passed.

The attached verification report records Linux Console/Mono publish and container archive checks. The image uses the same pinned LinuxServer Linux/AMD64 base as ai.15 and preserves fpcalc exactly once. Existing /config is not packaged or modified. No Docker daemon or Linux runtime is installed on the build host: Docker load, container startup, live-library scanning, browser/proxy behavior and third-party plugins require deployment-host verification. Percentages are work counters, not a timing estimate.

The Docker-loadable image, source, checksums and verification records are attached. No container registry image is implied.

To build on a Docker host: `docker build --platform linux/amd64 --build-arg VCS_REF="$(git rev-parse HEAD)" -t lidarr-ai:3.2.1.5196-ai.16 .`.
