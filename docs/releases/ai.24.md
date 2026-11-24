# Lidarr 3.2.1.5196-ai.24

Restores execute permission on the bundled `fpcalc` tool so Lidarr can calculate audio fingerprints. Includes the Unmapped Files retry changes from ai.23.

Load the Linux amd64 archive with `docker load -i lidarr-ai-3.2.1.5196-ai.24-amd64.tar.gz` and recreate your container with `lidarr-ai:3.2.1.5196-ai.24` and your existing appdata, database, and volume mappings.
