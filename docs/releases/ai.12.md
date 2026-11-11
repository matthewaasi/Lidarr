# Lidarr 3.2.1.5196-ai.12 — Queue refresh interval in seconds

In **Settings → Download Clients → Queue**, enter **Queue Refresh Interval (seconds)** and save. Advanced settings are not required. Accepted values are whole seconds from **1 to 86,400**. The default remains **60 seconds**.

Existing minute-based settings retain their duration: 1 minute becomes 60 seconds, 5 minutes becomes 300 seconds. The new saved seconds setting takes precedence. No database schema migration is required.

Saving updates the live queue schedule without a restart. The scheduler checks for due work every second. The interval begins after the prior scheduled refresh finishes; task queue delays and download-client response times can increase the observed interval. This setting controls server-side download-client polling. Held matching decisions still require Retry Matching or manual review.

## API

`GET/PUT /api/v1/config/downloadclient` uses `queueRefreshIntervalSeconds` in place of the old minute-based `queueRefreshInterval` field. Clients must update their request bodies. Zero, negative values and values above 86,400 are rejected; fractional seconds are not supported.

`GET /api/v1/system/task` continues to express `interval` in minutes, now allowing fractional values (15 seconds is `0.25`). `nextExecution` uses the actual seconds interval. Other scheduled jobs retain their existing minute-based schedules.

## Installation

1. Back up appdata and any external PostgreSQL database.
2. Load the ai.12 Docker archive: `docker load -i lidarr-ai-3.2.1.5196-ai.12-amd64.tar.gz`.
3. Set the container image to `lidarr-ai:3.2.1.5196-ai.12`, preserving existing mappings, ports, networking and database configuration.
4. Recreate/start the container and hard-refresh the browser.
5. Check the new field, save a value such as 15 or 30 seconds, and confirm refreshes against your download client.

## Build and verification

The application and UI are built from ai.11 source plus the ai.12 changes. Assembly version remains 3.2.1.5196 for plugin compatibility. The container archive is assembled with a local OCI layer tool using the exact LinuxServer base digest pinned in the Dockerfile, compiled Linux musl x64 binaries, production UI, and the base image's fpcalc. The application directory is replaced using an opaque OCI whiteout; existing /config is not included or changed.

See the build package's verification report for executed checks and limitations. Linux container startup, Docker loading and third-party plugin behavior require runtime verification on a Linux Docker host. No container registry image is published; use the Docker archive attached to the GitHub release.

To build conventionally on a Docker host: `docker build --platform linux/amd64 -t lidarr-ai:3.2.1.5196-ai.12 .`.

## Recorded verification

70 tests passed: 20 core, 10 API and 40 frontend. Production frontend build, Linux Console/Mono publish, changed-frontend lint and diff checks passed. Archive checks verify layer digests, executable permissions, binary/UI checksums and unique paths in every layer. The initial local archive contained a duplicate fpcalc path; the published archive removes that duplicate and retains the pinned runtime copy exactly once.
