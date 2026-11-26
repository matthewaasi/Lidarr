# Lidarr 3.2.1.5196-ai.25

The Library toolbar now offers **Preview Rename** and **Preview Retag**. Select artists to target those artists, or use the current Library filter. Clear the filter and selection to preview the entire Lidarr library. Open an artist in the preview to inspect the suggested path or tag changes and any skipped files. Apply queues the reviewed changes and checks them again before writing to disk.

In Library table view, enable the **Rename Files** and **Retag** columns in **Options**. Each column shows the latest saved check, including whether it needs to be repeated. Hover over a status for the last checked and last successfully applied dates, or click it to preview that artist. Existing artists start as **Not checked**. The Library reads saved checks and does not scan the music on page load.

Back up Lidarr appdata and any external PostgreSQL database before upgrading. Migration 84 adds the artist file status table. Load the Linux amd64 archive with `docker load -i lidarr-ai-3.2.1.5196-ai.25-amd64.tar.gz`, then recreate your container with image `lidarr-ai:3.2.1.5196-ai.25` and the existing appdata, database and volume mappings.
