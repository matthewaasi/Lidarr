# Lidarr 3.2.1.5196-ai.15 — Running task cancellation and named search progress

## Interactive search loading status

The popup now lists the configured indexers whose responses are still outstanding, updates that list as providers finish, displays **Processing release X/Y** during evaluation, and identifies final ranking. Original provider evaluation order, matching rules, deduplication, result availability and ranking are preserved. The ai.14 progressive batches and pages of 100 remain in use.

A provider that returns or fails is removed from the waiting list; errors continue to be logged. Provider fetching still returns whole batches. Counts are evaluated-report checkpoints and may differ from the final deduplicated result count. Reverse proxies must permit streaming and propagate client disconnects.

## Running task cancellation

**System → Tasks** now offers Cancel for running Album Search, Artist Search, Missing Album Search, Cutoff Unmet Album Search and Library Audit tasks. Accepted requests show **Cancelling…** while the task remains Started. It becomes Cancelled only after its handler stops at a safe point. Unsupported or finalizing tasks show a disabled cancel control with an explanation.

Cancellation affects only the selected run. Already completed work and queued downloads remain, and future scheduled runs are not disabled. In-flight plugin calls or current synchronous operations can finish before cancellation is observed. This does not terminate worker threads or cancel unsupported plugin task handlers.

Library audits check cancellation between files, after fingerprint lookup and between hashing chunks. An accepted cancellation before saving preserves previous findings and never publishes partial scan findings or resolves old findings. Once the final save begins, cancellation is unavailable until that phase finishes.

Queued cancellation is persisted, so cancelled queued tasks do not restart after an application restart. Running task rows remain visible until they end; late cancellation responses cannot resurrect a task already marked terminal. No database migration is added.

## Installation

1. Back up appdata and any external PostgreSQL database.
2. Load the image: `docker load -i lidarr-ai-3.2.1.5196-ai.15-amd64.tar.gz`.
3. Set the container image to `lidarr-ai:3.2.1.5196-ai.15`, preserving existing volumes, ports, network and database settings.
4. Recreate/start the container and hard-refresh the browser.
5. Check an interactive search for named waiting indexers and processing counts. Try cancelling a supported search or audit under System → Tasks; wait for Cancelled before starting conflicting work.

Assembly version remains 3.2.1.5196 for plugin compatibility. Previous matching, audit deletion and file-preview features remain available.

## API and implementation details

`DELETE /api/v1/command/{id}` returns the current command resource. `canCancel` identifies available cancellation and `cancellationRequested` distinguishes a pending stop request from terminal cancellation. SignalR updates preserve the running row until acknowledgement.

The progressive release stream adds `waiting` frames containing indexer IDs and names, plus a `ranking` frame. Existing `batch` and `complete` handling and the original release API are preserved. See [running-task details](https://github.com/matthewaasi/Lidarr/blob/v3.2.1.5196-ai.15/docs/RUNNING-TASK-CANCELLATION.md) and [search transport details](https://github.com/matthewaasi/Lidarr/blob/v3.2.1.5196-ai.15/docs/PROGRESSIVE-SEARCH.md).

## Verification and limits

129 tests passed: 63 frontend, 60 core and 6 API. Checks cover the real command executor, per-task isolation, pending versus acknowledged cancellation, queued persistence, preservation of audit findings, cancellation between hashing chunks, indexer names with out-of-order provider responses, release counts, final ranking, late responses and the existing 15,000-report synthetic matching parity case. Production UI build, analyzer-enabled backend builds, changed-file lint and diff checks passed.

The attached verification report records Linux Console/Mono publish and archive checks. The image uses the same pinned LinuxServer Linux/AMD64 base as ai.14 and preserves fpcalc exactly once. Existing /config is not packaged or modified. No Docker daemon or Linux runtime is installed on the build host: Docker load, container startup, live Soulseek data, browser/proxy behavior and third-party plugins require deployment-host verification. No real-provider timing improvement is claimed.

The Docker-loadable image, source, checksums and verification records are attached. No container registry image is implied.

To build on a Docker host: `docker build --platform linux/amd64 --build-arg VCS_REF="$(git rev-parse HEAD)" -t lidarr-ai:3.2.1.5196-ai.15 .`.
