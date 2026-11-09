using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Music.Singles
{
    // Matching tiers follow gergc/lidarr-plugin-uniquesingles (MIT); see LICENSES/UniqueSingles.txt.
    public static class SingleCoverage
    {
        public class Source
        {
            public Album Album { get; set; }
            public Track Track { get; set; }
            public TrackFile File { get; set; }
        }

        private static string Normalize(string title)
        {
            var value = Regex.Replace(title ?? string.Empty, @"\((?:feat\.?|featuring)\s[^)]*\)", string.Empty, RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"[!?.;:]+$", string.Empty);
            return Regex.Replace(value, @"\s+", " ").Trim().ToLowerInvariant();
        }

        private static string Versions(string title)
        {
            return string.Join("|", Regex.Matches(title ?? string.Empty, @"\b(live|remix|remastered?|radio|extended|acoustic|instrumental|demo|edit)\b", RegexOptions.IgnoreCase)
                .Select(m => m.Value.ToLowerInvariant()).Distinct().OrderBy(x => x));
        }

        public static SinglesEvidence Evaluate(Album single, string artist, List<Track> tracks, List<TrackFile> files, List<Source> sources, bool titleDuration, int tolerance, Func<string, bool> exists)
        {
            var evidence = new SinglesEvidence { Artist = artist, Single = single.Title, ForeignAlbumId = single.ForeignAlbumId, SingleFileIds = files.Select(f => f.Id).OrderBy(x => x).ToList() };
            var downloaded = sources.Where(s => s.Album.Id != single.Id && s.Album.Monitored && s.File != null &&
                s.Track.TrackFileId == s.File.Id && s.File.AlbumId == s.Album.Id).OrderBy(s => s.Album.Id).ThenBy(s => s.Track.Id).ToList();
            foreach (var track in tracks.OrderBy(t => t.Id))
            {
                var result = new SingleTrackEvidence { TrackId = track.Id, Title = track.Title, DurationMs = track.Duration, RecordingId = track.ForeignRecordingId, Reason = "No downloaded album/EP recording covers this track" };
                foreach (var source in downloaded)
                {
                    var sameId = !string.IsNullOrWhiteSpace(track.ForeignRecordingId) && string.Equals(track.ForeignRecordingId, source.Track.ForeignRecordingId, StringComparison.OrdinalIgnoreCase);
                    var sameTitle = Normalize(track.Title).Length > 0 && Normalize(track.Title) == Normalize(source.Track.Title);
                    if (!sameId && !sameTitle)
                    {
                        continue;
                    }

                    var duration = track.Duration > 0 && source.Track.Duration > 0 && Math.Abs((long)track.Duration - source.Track.Duration) <= tolerance;
                    var conflict = Versions(track.Title) != Versions(source.Track.Title) ||
                        (sameId && track.Duration > 0 && source.Track.Duration > 0 && !duration);
                    var tier = conflict ? 3 : sameId ? 1 : duration ? 2 : 3;
                    if ((result.Tier != 0 && result.Tier <= tier) || !exists(source.File.Path))
                    {
                        continue;
                    }

                    result.SourceTrackId = source.Track.Id;
                    result.SourceAlbumId = source.Album.Id;
                    result.SourceAlbum = source.Album.Title;
                    result.SourceForeignAlbumId = source.Album.ForeignAlbumId;
                    result.SourceTitle = source.Track.Title;
                    result.SourceDurationMs = source.Track.Duration;
                    result.SourceRecordingId = source.Track.ForeignRecordingId;
                    result.SourceFileId = source.File.Id;
                    result.SourcePath = source.File.Path;
                    result.SourceSize = source.File.Size;
                    result.SourceModified = source.File.Modified;
                    result.Tier = tier;
                    result.Reason = conflict ? "Recording/version or duration conflict; manual review required" : tier == 1 ? "Same MusicBrainz recording ID" : tier == 2 ? "Normalized title and duration agree" : "Title agrees but duration is missing or different; manual review required";
                }

                evidence.Tracks.Add(result);
            }

            var complete = tracks.Count > 0 && evidence.Tracks.All(t => t.Tier > 0);
            var mappedFiles = tracks.Where(t => t.HasFile).Select(t => t.TrackFileId).ToHashSet();
            var unmapped = files.Any(f => !mappedFiles.Contains(f.Id));
            var shared = files.Any(f => evidence.Tracks.Any(t => t.SourceFileId == f.Id || string.Equals(t.SourcePath, f.Path, StringComparison.OrdinalIgnoreCase)));
            evidence.CanCleanup = complete && !unmapped && !shared;
            evidence.Strong = evidence.CanCleanup && evidence.Tracks.All(t => t.Tier == 1 || (titleDuration && t.Tier == 2));
            evidence.Reason = shared ? "A single file is also a covering file; cleanup blocked" : unmapped ? "Single has unmapped files; map those files before cleanup" :
                !complete ? "Single contains tracks not covered by downloaded albums/EPs" : evidence.Strong ? "Every track is covered by downloaded album/EP recordings" : "Every track has a possible counterpart; manual review required";
            var fingerprint = JsonConvert.SerializeObject(new { single.Id, titleDuration, tolerance, evidence.Tracks, files = files.OrderBy(f => f.Id).Select(f => new { f.Id, f.Path, f.Size, f.Modified }) });
            evidence.Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
            return evidence;
        }
    }
}
