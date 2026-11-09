-- Read-only diagnostics. Does not display stored credentials or modify any rows.
-- Inspect a backup/copy of lidarr.db using your SQLite database viewer.
SELECT "Id", "Name", "Implementation", "ConfigContract", "Enable"
FROM "DownloadClients"
WHERE "ConfigContract" = 'DeemixSettings';
