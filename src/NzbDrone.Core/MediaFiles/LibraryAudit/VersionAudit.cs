using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.MediaFiles.LibraryAudit
{
    public static class VersionAudit
    {
        private const double DurationToleranceSeconds = 10;
        private static readonly Regex VersionSuffix = new Regex(@"(?:^|[\s\(\[])(extended(?:\s+(?:mix|version|edit))?|radio\s+(?:mix|version|edit)|original\s+(?:mix|version))[\)\]]?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string Version(string title)
        {
            var match = VersionSuffix.Match(title ?? string.Empty);
            if (!match.Success)
            {
                return string.Empty;
            }

            var value = match.Groups[1].Value.ToLowerInvariant();
            return value.StartsWith("extended", StringComparison.Ordinal) ? "extended" : value.StartsWith("radio", StringComparison.Ordinal) ? "radio" : "original";
        }

        public static string BaseTitle(string title) => Regex.Replace(VersionSuffix.Replace(title ?? string.Empty, string.Empty).ToLowerInvariant(), @"[^\p{L}\p{N}]", string.Empty);

        public static AuditEvidence Evaluate(AuditFile file, IEnumerable<Track> tracks)
        {
            var tagVersion = Version(file.TagTitle);
            var catalogVersion = Version(file.CatalogTitle);
            var filenameVersion = Version(Path.GetFileNameWithoutExtension(file.Path));
            var labels = new[] { tagVersion, catalogVersion, filenameVersion };
            var explicitConflict = labels.Where(x => x.Length > 0).Distinct().Count() > 1;
            var omission = labels.Any(x => x == "extended") && labels.Any(x => x.Length == 0);
            var durationDelta = file.DurationSeconds - file.CatalogDurationSeconds;
            var durationMismatch = double.IsFinite(file.DurationSeconds) && file.DurationSeconds > 0 &&
                double.IsFinite(file.CatalogDurationSeconds) && file.CatalogDurationSeconds > 0 &&
                Math.Abs(durationDelta) > DurationToleranceSeconds;
            var title = string.IsNullOrWhiteSpace(file.TagTitle) ? file.CatalogTitle : file.TagTitle;
            var baseTitle = BaseTitle(title);
            var audioCandidates = tracks.Where(x => file.AudioRecordingIds.Contains(x.ForeignRecordingId) &&
                x.ForeignRecordingId != file.RecordingId && Version(x.Title) != catalogVersion &&
                (Version(x.Title).Length > 0 || catalogVersion.Length > 0)).ToList();
            var candidates = baseTitle.Length == 0 || file.DurationSeconds <= 0 ? new List<AuditCandidate>() : tracks
                .Where(x => x.Duration > 0 && !string.IsNullOrWhiteSpace(x.ForeignRecordingId) && x.ForeignRecordingId != file.RecordingId &&
                    BaseTitle(x.Title) == baseTitle && Math.Abs((x.Duration / 1000.0) - file.DurationSeconds) <= 3 &&
                    Version(x.Title) != catalogVersion && (Version(x.Title).Length > 0 || catalogVersion.Length > 0))
                .GroupBy(x => x.ForeignRecordingId)
                .Select(x => new AuditCandidate { Title = x.First().Title, RecordingId = x.Key, DurationSeconds = x.First().Duration / 1000.0 })
                .OrderBy(x => x.RecordingId, StringComparer.Ordinal).ToList();

            var durationCandidateCount = candidates.Count;
            var audioConflict = file.AudioRecordingIds.Count > 0 && !file.AudioRecordingIds.Contains(file.RecordingId) && audioCandidates.Count > 0;
            if (!explicitConflict && !omission && !audioConflict && !durationMismatch)
            {
                return null;
            }

            var reason = new List<string>();
            if (audioConflict)
            {
                reason.Add("Fingerprint lookup supports other catalog recording(s) with different version labels and does not support the assigned recording. Crowd-sourced associations can be ambiguous; review before correcting.");
                candidates = audioCandidates.Select(x => new AuditCandidate { Title = x.Title, RecordingId = x.ForeignRecordingId, DurationSeconds = x.Duration / 1000.0 })
                    .Concat(candidates).GroupBy(x => x.RecordingId).Select(x => x.First()).OrderBy(x => x.RecordingId, StringComparer.Ordinal).ToList();
            }

            if (explicitConflict)
            {
                reason.Add("Filename, embedded title and catalog title name conflicting versions.");
            }

            if (omission)
            {
                reason.Add("An extended label is present in some titles and absent from others. An omitted label does not prove a different mix.");
            }

            if (durationMismatch)
            {
                var direction = durationDelta > 0 ? "longer" : "shorter";
                reason.Add($"The file is {Math.Abs(durationDelta):F1} seconds {direction} than the assigned catalog track.");
            }

            if (durationCandidateCount > 0)
            {
                reason.Add("Other catalog versions for this artist and title are within 3 seconds of this file. Duration is supporting evidence, not audio identification.");
            }

            return new AuditEvidence
            {
                Confidence = audioConflict ? "supported" : "uncertain",
                Reason = string.Join(" ", reason),
                SuggestedAction = "Listen to the file and verify its album and track assignment. Compare any listed candidates before changing the assignment or tags.",
                Files = new List<AuditFile> { file },
                Candidates = candidates
            };
        }
    }
}
