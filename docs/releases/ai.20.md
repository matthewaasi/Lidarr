# Lidarr 3.2.1.5196-ai.20

Settings > Identification now separates automatic matching from Manual Import. Existing installs default to Same as automatic. Choose Original Lidarr for Manual Import to keep LM Studio out of both preview and Map Tracks while retaining AI fallback for automatic downloads.

Opening Manual Import never sends a fresh LM Studio request. AI-capable preview methods may show saved decisions. Map Tracks uses the manual method and can call LM Studio when that method allows it. Retry Matching uses the automatic method.

Load the attached Linux amd64 image with `docker load -i lidarr-ai-3.2.1.5196-ai.20-amd64.tar.gz`, then recreate your Lidarr container with `lidarr-ai:3.2.1.5196-ai.20` and the existing appdata, database and volume mappings.
