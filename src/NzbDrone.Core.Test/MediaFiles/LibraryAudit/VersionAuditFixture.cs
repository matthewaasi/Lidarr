using System;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.LibraryAudit;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Test.MediaFilesTests.LibraryAudit
{
    [TestFixture]
    public class VersionAuditFixture
    {
        private AuditFile _file;

        [SetUp]
        public void Setup()
        {
            _file = new AuditFile
            {
                Id = 1,
                Path = "/music/Song (Extended Mix).flac",
                CatalogTitle = "Song (Extended Mix)",
                TagTitle = "Song (Extended Mix)",
                CatalogDurationSeconds = 348,
                DurationSeconds = 348,
                RecordingId = "extended"
            };
        }

        [TestCase("Song (Extended Mix)", "extended")]
        [TestCase("Song [RADIO EDIT]", "radio")]
        [TestCase("Song - Original Mix", "original")]
        [TestCase("Song (DJ Remix)", "")]
        [TestCase("Unextended", "")]
        [TestCase(null, "")]
        public void recognizes_only_explicit_version_suffixes(string title, string expected)
        {
            Assert.That(VersionAudit.Version(title), Is.EqualTo(expected));
        }

        [Test]
        public void consistent_extended_mix_has_no_finding()
        {
            Assert.That(VersionAudit.Evaluate(_file, Array.Empty<Track>()), Is.Null);
        }

        [Test]
        public void short_file_assigned_to_extended_can_suggest_radio_but_cannot_prove_it()
        {
            _file.DurationSeconds = 192;
            var evidence = VersionAudit.Evaluate(_file, new[]
            {
                new Track { Title = "Song (Radio Edit)", Duration = 193000, ForeignRecordingId = "radio" },
                new Track { Title = "Other Song (Radio Edit)", Duration = 192000, ForeignRecordingId = "other" },
                new Track { Title = "Song (DJ Remix)", Duration = 192000, ForeignRecordingId = "remix" }
            });
            Assert.That(evidence.Confidence, Is.EqualTo("uncertain"));
            Assert.That(evidence.Candidates.Count, Is.EqualTo(1));
            Assert.That(evidence.Candidates[0].RecordingId, Is.EqualTo("radio"));
            Assert.That(evidence.Reason, Does.Contain("156.0 seconds"));
        }

        [Test]
        public void unlabeled_file_assigned_to_radio_can_suggest_extended()
        {
            _file.Path = "/music/Song.flac";
            _file.TagTitle = "Song";
            _file.CatalogTitle = "Song (Radio Edit)";
            _file.CatalogDurationSeconds = 192;
            _file.RecordingId = "radio";
            var evidence = VersionAudit.Evaluate(_file, new[] { new Track { Title = "Song (Extended Mix)", Duration = 348000, ForeignRecordingId = "extended" } });
            Assert.That(evidence.Candidates[0].RecordingId, Is.EqualTo("extended"));
        }

        [Test]
        public void label_omission_is_reported_as_uncertain_even_when_duration_agrees()
        {
            _file.TagTitle = "Song";
            var evidence = VersionAudit.Evaluate(_file, Array.Empty<Track>());
            Assert.That(evidence.Confidence, Is.EqualTo("uncertain"));
            Assert.That(evidence.Reason, Does.Contain("omitted label does not prove"));
        }

        [Test]
        public void conflicting_explicit_labels_are_reported()
        {
            _file.TagTitle = "Song (Radio Edit)";
            Assert.That(VersionAudit.Evaluate(_file, Array.Empty<Track>()).Reason, Does.Contain("conflicting versions"));
        }

        [Test]
        public void unknown_duration_does_not_generate_candidates()
        {
            _file.DurationSeconds = 0;
            Assert.That(VersionAudit.Evaluate(_file, new[] { new Track { Title = "Song (Radio Edit)", Duration = 1000, ForeignRecordingId = "radio" } }), Is.Null);
        }

        [Test]
        public void long_unlabeled_track_is_not_automatically_an_extended_mix()
        {
            _file.CatalogTitle = _file.TagTitle = "Song";
            _file.Path = "/music/Song.flac";
            _file.DurationSeconds = 700;
            _file.CatalogDurationSeconds = 200;
            Assert.That(VersionAudit.Evaluate(_file, Array.Empty<Track>()), Is.Null);
        }

        [Test]
        public void fingerprint_can_flag_consistently_mislabeled_audio()
        {
            _file.AudioRecordingIds.Add("radio");
            var evidence = VersionAudit.Evaluate(_file, new[] { new Track { Title = "Song (Radio Edit)", ForeignRecordingId = "radio", Duration = 192000 } });
            Assert.That(evidence.Confidence, Is.EqualTo("supported"));
            Assert.That(evidence.Candidates[0].RecordingId, Is.EqualTo("radio"));
        }

        [Test]
        public void ambiguous_fingerprint_that_includes_current_recording_does_not_prove_conflict()
        {
            _file.AudioRecordingIds.AddRange(new[] { "extended", "radio" });
            Assert.That(VersionAudit.Evaluate(_file, new[] { new Track { Title = "Song (Radio Edit)", ForeignRecordingId = "radio", Duration = 192000 } }), Is.Null);
        }
    }
}
