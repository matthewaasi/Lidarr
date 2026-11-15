# Lidarr 3.2.1.5196-ai.13 for Unraid

Includes **Library → Audit**, version/duration checks, optional AcoustID evidence, exact-file duplicates, saved review decisions, and library/artist page performance improvements. Existing native/LM Studio identification, Singles Review, and settings are retained. Audit scans do not change music files, tags, assignments, or monitoring.

## Upgrade an existing container

1. Download `lidarr-ai-3.2.1.5196-ai.13-amd64.tar.gz` and `SHA256SUMS.txt` from the GitHub release while signed into the private repository. Copy the image archive to an Unraid share, for example `/mnt/user/system/lidarr-ai/`.
2. Before upgrading, stop the existing container and back up its `/config` appdata and PostgreSQL databases. **This release adds main-database migration 83.** An appdata copy alone does not back up an external PostgreSQL library. Keep the pre-upgrade database backup for rollback.
3. Load the image from an Unraid terminal:

   ```sh
   docker load -i /mnt/user/system/lidarr-ai/lidarr-ai-3.2.1.5196-ai.13-amd64.tar.gz
   ```

4. In **Docker → lidarr-ai → Edit**, set **Repository** to `lidarr-ai:3.2.1.5196-ai.13`. Preserve all existing paths, network/port mappings, environment variables, and PostgreSQL settings. Apply and start.
5. Hard-refresh the browser. Open **Library → Audit** to confirm the feature is present. Scans are manual; no audit starts automatically. Existing Unique Singles settings are not changed.

The application listens on container port **8686**. The example template maps host port **8687**, matching the existing instance. The tag is loaded locally; no registry pull is required.

## New container

Load the image first. Copy `my-lidarr-ai.xml` to `/boot/config/plugins/dockerMan/templates-user/` and select the template from Unraid's Add Container page, or adapt `compose.yaml`. Change the example media/download paths to your actual mappings. Use a separate appdata directory if an existing instance must remain running.

## Build from source

```sh
docker build --build-arg FORK_VERSION=3.2.1.5196-ai.13 --build-arg VCS_REF="$(git rev-parse HEAD)" -t lidarr-ai:3.2.1.5196-ai.13 .
```

The source Dockerfile uses .NET SDK 8.0.421 and a pinned LinuxServer Lidarr base. The supplied archive is Linux/AMD64 and uses a self-contained .NET 8 application bundle. Read `BUILD-REPORT.md` for the actual packaging toolchain and verification limits.

## Performance expectations

The prior artist-list API measured approximately 18 seconds initially and 4 seconds on repeat for 2,211 artists. The update reduces repeated statistics work and redundant JSON, preserves frontend memoization, and displays albums before track-file loading completes. Actual end-to-end improvement must be measured after installation; the release's synthetic benchmark is not a live speed guarantee.

See `docs/LIBRARY-AUDIT.md` and `docs/UI-PERFORMANCE.md` in the source archive for the feature behavior and measurements.
