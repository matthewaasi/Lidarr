using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.TrackImport.Identification
{
    public interface IAiMatchAudit
    {
        void Write(LocalAlbumRelease release);
    }

    public class AiMatchAudit : IAiMatchAudit
    {
        private static readonly object WriteLock = new object();
        private readonly IAppFolderInfo _folders;
        private readonly Logger _logger;

        public AiMatchAudit(IAppFolderInfo folders, Logger logger)
        {
            _folders = folders;
            _logger = logger;
        }

        public void Write(LocalAlbumRelease release)
        {
            try
            {
                var entry = JsonConvert.SerializeObject(new
                {
                    timestamp = DateTime.UtcNow,
                    release = release.AiMatch,
                    files = release.LocalTracks.Select(f => new { filename = Path.GetFileName(f.Path), decision = f.AiMatch })
                });
                lock (WriteLock)
                {
                    var folder = Path.Combine(_folders.AppDataFolder, "logs");
                    Directory.CreateDirectory(folder);
                    var path = Path.Combine(folder, "ai-matching.jsonl");
                    if (File.Exists(path) && new FileInfo(path).Length > 10 * 1024 * 1024)
                    {
                        File.Move(path, path + ".1", true);
                    }

                    File.AppendAllText(path, entry + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("Unable to write AI matching audit ({0})", ex.GetType().Name);
            }
        }
    }
}
