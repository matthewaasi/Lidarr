# Lidarr 3.2.1.5196-ai.28

Under **Library → Audit**, **Track duration and version audit** now flags any single-track file whose measured duration differs from its assigned catalog track by more than 10 seconds in either direction. Findings show how much shorter or longer the file is. Both durations must be known. Version labels, possible alternate mixes, and optional AcoustID evidence are still available. Run a new audit to discover the additional findings.

Load the Linux amd64 archive with `docker load -i lidarr-ai-3.2.1.5196-ai.28-amd64.tar.gz`, then recreate your container with `lidarr-ai:3.2.1.5196-ai.28` and your existing appdata, database and volume mappings.
