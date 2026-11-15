# Lidarr 3.2.1.5196-ai.14 — Progressive interactive search and cancellation

Album and artist interactive searches now display fully evaluated releases in batches, with processed/total progress. Results and ranking are labelled provisional until the final list arrives. The existing matching specifications, GUID deduplication and final download-decision ranking remain in use. No results are capped, and no matching thresholds are relaxed.

Results display in pages of 100 to avoid rendering thousands of rows at once. Every returned result remains available through pagination, sorting and filtering. Completed download-button state is retained when final ranking arrives.

Closing the results window aborts its request and cancels further server evaluation. The provider API returns whole batches, so fetching must finish before checked results can stream. An in-flight provider call or a currently executing synchronous operation may finish before cancellation is observed. Already queued downloads continue. Reopening starts a fresh search. Interrupted searches retain partial results with an explicit error and are never labelled complete.

This improves time to review during evaluation and limits browser rendering work. It does not promise faster provider fetching or shorter total evaluation time. Reverse proxies must allow streaming and propagate disconnects; `X-Accel-Buffering: no` is sent. See [progressive search details](https://github.com/matthewaasi/Lidarr/blob/v3.2.1.5196-ai.14/docs/PROGRESSIVE-SEARCH.md).

## Installation

1. Back up appdata and any external PostgreSQL database.
2. Load the image: `docker load -i lidarr-ai-3.2.1.5196-ai.14-amd64.tar.gz`.
3. Set the container image to `lidarr-ai:3.2.1.5196-ai.14`, preserving existing volumes, ports, network and database settings.
4. Recreate/start the container and hard-refresh the browser.
5. Try an interactive search, confirm that checked batches and progress appear, then close a running search and check that further release evaluation stops. A plugin request already in flight can finish internally.

No database migration is added. Assembly version remains 3.2.1.5196 for plugin compatibility. All ai.13 audit deletion and file-preview features are retained.

## API and compatibility

`GET /api/v1/release/stream?albumId=<id>` or `?artistId=<id>` accepts exactly one positive ID and returns flushed newline-delimited JSON frames: `searching`, `batch`, `complete` or `error`. Batches contain processed/total counts and changed checked records. Completion contains the final deduplicated, prioritized list. Browser request cancellation propagates to server evaluation. The original `GET /api/v1/release` and automatic-search behavior remain available.

## Recorded verification

88 tests passed: 57 frontend, 27 core and 4 API tests. Synthetic parity compared 15,000 reports through regular and progressive evaluation, including matching decisions, rejection reasons, qualities, custom format scores and ordering. Tests cover cancellation during fetching and between evaluated batches, final API ordering, incomplete streams, late responses after cancellation, and bounded rendering with access to all results. Production UI build, analyzer-enabled backend builds, changed-file lint and diff checks passed. The test harness initializes its platform dependency and restores logging assertions after AutoMoq setup.

The attached verification report records Linux Console/Mono publish and container archive checks. The image uses the same pinned LinuxServer Linux/AMD64 base as ai.13 and preserves fpcalc exactly once. Existing /config is not packaged or modified. No Docker daemon or Linux runtime is installed on the build host: Docker load, container startup, live Soulseek behavior, browser/proxy integration and third-party plugins require verification on the deployment host. The parity tests use synthetic reports; no real-provider timing improvement is claimed.

The Docker-loadable image, source, checksums and verification records are attached. No container registry image is implied.

To build on a Docker host: `docker build --platform linux/amd64 --build-arg VCS_REF="$(git rev-parse HEAD)" -t lidarr-ai:3.2.1.5196-ai.14 .`.
