using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.MediaFiles.TrackImport.Identification;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Identification
{
    [TestFixture]
    public class AiImportDecisionCacheFixture
    {
        private string _folder;
        private Mock<IAppFolderInfo> _folders;
        private Mock<IReleaseService> _releases;
        private Mock<ITrackService> _tracks;
        private AiImportDecisionCache _cache;
        private LocalTrack _file;
        private AlbumRelease _release;
        private Track _track;

        [SetUp]
        public void Setup()
        {
            _folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ai-cache-" + Guid.NewGuid().ToString("N"));
            _folders = new Mock<IAppFolderInfo>();
            _folders.SetupGet(f => f.AppDataFolder).Returns(_folder);
            _releases = new Mock<IReleaseService>();
            _tracks = new Mock<ITrackService>();
            _release = new AlbumRelease { Id = 20, Album = new Album { Id = 10, Artist = new Artist { Id = 1 } } };
            _track = new Track { Id = 30, AlbumReleaseId = 20, Title = "A track" };
            _releases.Setup(r => r.GetRelease(20)).Returns(_release);
            _tracks.Setup(t => t.GetTracks(It.IsAny<IEnumerable<int>>())).Returns(new List<Track> { _track });
            _cache = NewCache();
            _file = new LocalTrack
            {
                Path = "/downloads/album/song.flac",
                Size = 12345,
                Modified = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc),
                Release = _release,
                Tracks = new List<Track> { _track },
                AiMatch = new AiMatchResult { Proposed = true, Reason = "Review mode: Same recording", Model = "test-model", Confidence = 0.99 }
            };
        }

        private AiImportDecisionCache NewCache()
        {
            return new AiImportDecisionCache(_folders.Object, _releases.Object, _tracks.Object, LogManager.GetLogger("cache-test"));
        }

        private LocalTrack PreviewFile()
        {
            return new LocalTrack { Path = _file.Path, Size = _file.Size, Modified = _file.Modified };
        }

        private void Save()
        {
            _cache.Save(new List<ImportDecision<LocalTrack>> { new ImportDecision<LocalTrack>(_file, new Rejection("AI manual review: Same recording")) });
        }

        [TearDown]
        public void Cleanup()
        {
            if (Directory.Exists(_folder))
            {
                File.Delete(Path.Combine(_folder, "ai-import-decisions.json"));
                File.Delete(Path.Combine(_folder, "ai-import-decisions.json.tmp"));
                Directory.Delete(_folder);
            }
        }

        [Test]
        public void should_restore_saved_proposal_and_rejection_after_service_restart()
        {
            Save();
            var result = NewCache().Read(new List<LocalTrack> { PreviewFile() }).Single();
            Assert.That(result.Item.Tracks.Single().Id, Is.EqualTo(30));
            Assert.That(result.Item.Artist.Id, Is.EqualTo(1));
            Assert.That(result.Item.AiMatch.IsCached, Is.True);
            Assert.That(result.Item.AiMatch.Proposed, Is.True);
            Assert.That(result.Approved, Is.False);
            Assert.That(result.Rejections.Single().Reason, Does.Contain("Same recording"));
        }

        [Test]
        public void missing_cache_should_return_actionable_unmatched_result()
        {
            var result = _cache.Read(new List<LocalTrack> { PreviewFile() }).Single();
            Assert.That(result.Item.Tracks, Is.Empty);
            Assert.That(result.Item.AiMatch.Proposed, Is.False);
            Assert.That(result.Rejections.Single().Reason, Does.Contain("Retry AI"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void changed_file_must_not_reuse_old_proposal(bool changeSize)
        {
            Save();
            var file = PreviewFile();
            if (changeSize)
            {
                file.Size++;
            }
            else
            {
                file.Modified = file.Modified.AddSeconds(1);
            }

            var result = _cache.Read(new List<LocalTrack> { file }).Single();
            Assert.That(result.Item.Tracks, Is.Empty);
            Assert.That(result.Item.AiMatch.Proposed, Is.False);
        }

        [Test]
        public void rejected_ai_request_should_remain_visible_without_track_mapping()
        {
            _file.Release = null;
            _file.Tracks.Clear();
            _file.AiMatch = new AiMatchResult { Reason = "AI matching failed: LM Studio returned HTTP 400" };
            Save();
            var result = _cache.Read(new List<LocalTrack> { PreviewFile() }).Single();
            Assert.That(result.Item.AiMatch.Reason, Does.Contain("HTTP 400"));
            Assert.That(result.Item.Tracks, Is.Empty);
        }

        [Test]
        public void missing_or_cross_release_database_track_must_invalidate_cached_proposal()
        {
            Save();
            _track.AlbumReleaseId = 99;
            var result = _cache.Read(new List<LocalTrack> { PreviewFile() }).Single();
            Assert.That(result.Item.Tracks, Is.Empty);
            Assert.That(result.Item.AiMatch.Proposed, Is.False);
        }

        [Test]
        public void corrupted_cache_must_not_prevent_manual_review()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "ai-import-decisions.json"), "invalid json");
            var result = _cache.Read(new List<LocalTrack> { PreviewFile() }).Single();
            Assert.That(result.Approved, Is.False);
            Assert.That(result.Item.Tracks, Is.Empty);
        }
    }
}
