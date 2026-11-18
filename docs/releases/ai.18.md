# Lidarr 3.2.1.5196-ai.18 — Batched logging and indexed matching

This release addresses work still limiting interactive and automatic searches after ai.17. Debug and trace logs previously opened, flushed and closed the appdata file synchronously for every message. They now use bounded asynchronous batches, retaining credential cleansing, rotation and graceful flushing. Info logging remains synchronous. The queue blocks rather than silently dropping messages when full. Abrupt process termination can lose queued log messages.

Artist and album catalogs are indexed within each search, normalized artist aliases are reused, and criteria regexes are reused by full pattern and culture. Unique fuzzy-artist selection no longer sorts the entire catalog. Scoring passes, thresholds, candidates, duplicate/ambiguity handling, album-year rules and the parsing order are retained. Interactive searches still retain full rejection details; ai.17's permanent-rejection shortcuts still apply to automatic searches.

Search timing summaries now appear approximately every five seconds at completed processing-window boundaries and when a search completes or stops. A slow window can delay a summary. Stopped counts include fully evaluated windows. Stage timings include nested/concurrent work and logging waits; they must not be summed as wall time or interpreted as SQL-only durations.

## Install

1. Back up appdata and any external PostgreSQL database.
2. Load `lidarr-ai-3.2.1.5196-ai.18-amd64.tar.gz` with `docker load -i lidarr-ai-3.2.1.5196-ai.18-amd64.tar.gz`.
3. Set the existing container image to `lidarr-ai:3.2.1.5196-ai.18`, preserving existing volumes, paths, ports, network, plugins and PostgreSQL settings, then recreate/start it.
4. Preserve `LIDARR_RELEASE_WORKERS=8` if already configured. The range remains 1–16; the default is visible CPU count capped at four. Use one worker to diagnose third-party plugin concurrency issues.
5. Repeat a large search and compare `Release processing progress`, `completed` and `stopped` summaries. They include counts, throughput, workers, cache hits/misses and cumulative stage timings.

No new database migration is required. Catalog caches last for a search; concurrent edits/imports can be reflected on the next search. Assembly version stays 3.2.1.5196 for plugin compatibility. The attached Docker-loadable Linux amd64 archive uses the same pinned LinuxServer runtime as ai.17. No container registry image or automatic deployment is implied.

## Verification and limits

421 backend tests passed with two existing skips. Ten search decision/parity/cancellation checks also passed with eight workers. Tests verify ordered decisions, per-search isolation, SQLite exact-lookup parity, duplicates, ambiguity, cancelled-search summaries, and preservation/redaction/rotation of log entries.

Private local replay compared 5,172 title parses (5,168 captured plus fixture examples), including every serialized parsed field, with no differences. It compared 2,428 original/indexed artist lookups (2,419 captured plus fixture examples), with no match differences against a 134-artist fixture catalog. Captured data is not included in the repository or published verification records.

Local component timings were about 1.6x faster for criteria parsing and 2.4x faster for fuzzy lookup in that run. These are component tests, not a production search speedup or proof of equivalent accuracy against your actual library. The 500 releases/sec goal remains unverified. See [performance details](../SEARCH-PERFORMANCE.md).

Production frontend compilation/tests, analyzer-enabled Linux Console/Mono publishing and Docker-archive integrity checks are recorded in the attached verification report. No Docker daemon or Linux runtime is available on the build host: Docker loading, container startup, live PostgreSQL and third-party plugins require deployment-host validation. No running server configuration was changed.

Build on a Docker host with `docker build --platform linux/amd64 --build-arg VCS_REF="$(git rev-parse HEAD)" -t lidarr-ai:3.2.1.5196-ai.18 .`.
