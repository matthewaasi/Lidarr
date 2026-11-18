# Lidarr 3.2.1.5196-ai.17 — Faster release evaluation

Interactive and automatic searches now share repeated lookup work within a search and evaluate releases with bounded parallel workers. Exact and fuzzy artist/album matching (including misses), artist album candidates, album files, missing tracks and album history are read once per distinct key per search. Album matching keys retain artist, raw title and year. Lists returned to callers are independent copies; cached entities are shared, with synchronized lazy loading.

Results retain input order, custom-format calculation and final ranking. Interactive searches retain full rejection explanations in their original order. Automatic searches prioritize quality/requested-album/title eligibility within the existing priority groups and stop on a permanent rejection. They may show fewer reasons for an already permanently rejected result. Temporary rejections do not hide permanent rejections in the same group. RSS processing retains its serial path.

The cache is a read-once view per lookup, not a PostgreSQL transaction snapshot. Edits, imports or refreshes after a lookup has loaded are reflected on the next search. Caches end before automatic download submission. Imports and refreshes do not use these search caches.

## Installation and tuning

1. Back up appdata and any external PostgreSQL database.
2. Load the attached image: `docker load -i lidarr-ai-3.2.1.5196-ai.17-amd64.tar.gz`.
3. Set the existing container image to `lidarr-ai:3.2.1.5196-ai.17`, preserving volumes, paths, ports, network, plugins and database settings, then recreate/start it.
4. Optionally set `LIDARR_RELEASE_WORKERS` to a whole number from 1–16. The default is the available processor count capped at four. Start with the default; compare 4, 8 and 16 on a large machine. Use `1` to serialize evaluation when diagnosing a third-party plugin's concurrency behavior. Out-of-range integer values are clamped; invalid text uses the default.
5. Repeat the same large search. Look for `Release processing completed` in the log. It records reports, seconds, releases/sec, worker count, cache hits/misses and the largest cumulative worker stage timings. Timings overlap and nest, so they must not be summed as wall time or interpreted as SQL-only time. Callback/output time is included in wall time. Compare metadata-refresh-idle and active runs separately.

No new schema migration or server configuration change is required. The Docker archive uses the same pinned LinuxServer Linux/AMD64 runtime. Assembly version remains 3.2.1.5196 for plugin compatibility. No registry image is implied by the attached Docker-loadable archive.

## Verification and performance limits

406 backend tests passed with two existing skips; all 70 frontend tests passed. Nine search-processing tests also passed with one worker and again with eight workers. Checks include analyzer-enabled backend compilation, serial/parallel decision parity for 15,000 reports, a 10,000-report synthetic comparison, cache isolation and null-match coalescing, independent result lists, concurrent lazy loading, automatic permanent/temporary rejection checks and progressive cancellation. Benchmark results and build/container checks are in the attached verification report and records.

Synthetic benchmarks use mocked database/mapping dependencies. Their releases/sec is not a measured improvement on a production library. The 500 releases/sec goal requires a repeated server-side search measurement after deployment. Existing logs demonstrated roughly 5.8–7.4 releases/sec under concurrent workload; those historical runs were not repeated on this build host.

No Docker daemon or Linux runtime is available on the build host. The archive is inspected for platform, entrypoint, volume, tag/revision, layer hashes, application contents, executable modes and preserved fpcalc. Docker loading, container startup, PostgreSQL integration and external plugin concurrency require deployment-host verification. This release does not modify your running server.

The separate Soulseek cleanup HTTP 405 loop is not repaired here: the stack traces refer to Slskd client code not present in this repository. No third-party plugin binary was patched. Metadata refresh scheduling is also unchanged.

Build on a Docker host with `docker build --platform linux/amd64 --build-arg VCS_REF="$(git rev-parse HEAD)" -t lidarr-ai:3.2.1.5196-ai.17 .`.
