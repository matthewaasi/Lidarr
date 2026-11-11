using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.TrackImport.Identification
{
    public interface IAiImportDecisionCache
    {
        void Save(List<ImportDecision<LocalTrack>> decisions);
        List<ImportDecision<LocalTrack>> Read(List<LocalTrack> files);
    }

    public class AiImportDecisionCache : IAiImportDecisionCache
    {
        private static readonly object CacheLock = new object();
        private readonly IAppFolderInfo _folders;
        private readonly IReleaseService _releases;
        private readonly ITrackService _tracks;
        private readonly Logger _logger;

        public AiImportDecisionCache(IAppFolderInfo folders, IReleaseService releases, ITrackService tracks, Logger logger)
        {
            _folders = folders;
            _releases = releases;
            _tracks = tracks;
            _logger = logger;
        }

        private string CachePath => Path.Combine(_folders.AppDataFolder, "ai-import-decisions.json");

        private Dictionary<string, CachedAiImportDecision> Load()
        {
            if (!File.Exists(CachePath) || new FileInfo(CachePath).Length > 20 * 1024 * 1024)
            {
                return new Dictionary<string, CachedAiImportDecision>(StringComparer.Ordinal);
            }

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, CachedAiImportDecision>>(File.ReadAllText(CachePath)) ??
                    new Dictionary<string, CachedAiImportDecision>(StringComparer.Ordinal);
            }
            catch (JsonException)
            {
                _logger.Warn("AI import decision cache is invalid; fresh results will replace it");
                return new Dictionary<string, CachedAiImportDecision>(StringComparer.Ordinal);
            }
        }

        public void Save(List<ImportDecision<LocalTrack>> decisions)
        {
            try
            {
                lock (CacheLock)
                {
                    var entries = Load();
                    foreach (var decision in decisions.Where(d => d.Item?.AiMatch != null))
                    {
                        var file = decision.Item;
                        entries[file.Path] = new CachedAiImportDecision
                        {
                            Size = file.Size,
                            Modified = file.Modified,
                            SavedAt = DateTime.UtcNow,
                            ReleaseId = file.Release?.Id ?? 0,
                            TrackIds = file.Tracks.Select(t => t.Id).ToList(),
                            AiMatch = file.AiMatch,
                            Rejections = decision.Rejections.Select(r => r.Reason).ToList()
                        };
                    }

                    entries = entries.OrderByDescending(e => e.Value.SavedAt).Take(5000).ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
                    Directory.CreateDirectory(_folders.AppDataFolder);
                    File.WriteAllText(CachePath + ".tmp", JsonConvert.SerializeObject(entries));
                    File.Move(CachePath + ".tmp", CachePath, true);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("Could not save AI import decisions ({0})", ex.GetType().Name);
            }
        }

        public List<ImportDecision<LocalTrack>> Read(List<LocalTrack> files)
        {
            Dictionary<string, CachedAiImportDecision> entries;
            try
            {
                lock (CacheLock)
                {
                    entries = Load();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("Could not read AI import decisions ({0})", ex.GetType().Name);
                entries = new Dictionary<string, CachedAiImportDecision>();
            }

            var result = new List<ImportDecision<LocalTrack>>();
            foreach (var file in files)
            {
                try
                {
                    if (!entries.TryGetValue(file.Path, out var cached) || cached.Size != file.Size || cached.Modified != file.Modified || cached.AiMatch == null)
                    {
                        result.Add(Unassessed(file));
                        continue;
                    }

                    var release = cached.ReleaseId > 0 ? _releases.GetRelease(cached.ReleaseId) : null;
                    var tracks = cached.TrackIds.Count > 0 ? _tracks.GetTracks(cached.TrackIds) : new List<Track>();
                    if (tracks.Count != cached.TrackIds.Count || tracks.Any(t => t.AlbumReleaseId != cached.ReleaseId) ||
                        (cached.AiMatch.Proposed && (release == null || tracks.Count == 0)))
                    {
                        result.Add(Unassessed(file));
                        continue;
                    }

                    file.Release = release;
                    file.Album = release?.Album.Value;
                    file.Artist = file.Album?.Artist.Value;
                    file.Tracks = tracks;
                    file.Distance = new Distance();
                    file.AiMatch = cached.AiMatch;
                    file.AiMatch.IsCached = true;
                    result.Add(new ImportDecision<LocalTrack>(file, cached.Rejections.Select(r => new Rejection(r)).ToArray()));
                }
                catch (Exception ex)
                {
                    _logger.Debug("Cached AI proposal no longer usable ({0})", ex.GetType().Name);
                    result.Add(Unassessed(file));
                }
            }

            return result;
        }

        private static ImportDecision<LocalTrack> Unassessed(LocalTrack file)
        {
            file.Tracks = new List<Track>();
            file.Release = null;
            file.Album = null;
            file.Artist = null;
            file.AiMatch = new AiMatchResult
            {
                IsCached = true,
                Reason = "No current saved AI result. Use Queue Refresh or select files and click Map Tracks to request matching."
            };
            return new ImportDecision<LocalTrack>(file, new Rejection(file.AiMatch.Reason));
        }
    }

    public class CachedAiImportDecision
    {
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        public DateTime SavedAt { get; set; }
        public int ReleaseId { get; set; }
        public List<int> TrackIds { get; set; } = new List<int>();
        public AiMatchResult AiMatch { get; set; }
        public List<string> Rejections { get; set; } = new List<string>();
    }
}
