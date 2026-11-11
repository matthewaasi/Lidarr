using System;
using NUnit.Framework;
using NzbDrone.Core.Music.Singles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.Singles
{
    [TestFixture]
    public class SinglesDecisionRepositoryFixture : DbTest<SinglesDecisionRepository, SingleDecision>
    {
        [Test]
        public void migration_and_repository_should_persist_review_and_monitoring_ownership()
        {
            var row = Subject.Insert(new SingleDecision { AlbumId = 10, ArtistId = 1, Status = "pending", AutoUnmonitored = false, Evidence = "{}", Updated = DateTime.UtcNow });
            row.AutoUnmonitored = true;
            row.Status = "cleaned";
            Subject.Update(row);
            Assert.That(Subject.ForAlbum(10).AutoUnmonitored, Is.True);
            Assert.That(Subject.ForArtist(1).Count, Is.EqualTo(1));
            Assert.That(Subject.ForAlbum(10).Status, Is.EqualTo("cleaned"));
        }
    }
}
