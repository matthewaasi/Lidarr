using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Api.V1.Albums;
using Lidarr.Api.V1.Artist;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Music;

namespace NzbDrone.Api.Test.Albums
{
    [TestFixture]
    public class AlbumSummaryFixture
    {
        private sealed class Unavailable<T> : LazyLoaded<T>
        {
            public override void LazyLoad()
            {
                throw new InvalidOperationException("Summary must not load nested artist or releases");
            }
        }

        [Test]
        public void library_summary_retains_navigation_and_dates_without_loading_nested_records()
        {
            var album = new Album
            {
                Id = 10, ForeignAlbumId = "album-id", Title = "Album", Disambiguation = "edition", ReleaseDate = new DateTime(2020, 1, 1),
                Artist = new Unavailable<Artist>(), AlbumReleases = new Unavailable<List<AlbumRelease>>()
            };
            var summary = album.ToSummaryResource();
            Assert.That(summary.Id, Is.EqualTo(10));
            Assert.That(summary.ForeignAlbumId, Is.EqualTo("album-id"));
            Assert.That(summary.Title, Is.EqualTo("Album"));
            Assert.That(summary.Disambiguation, Is.EqualTo("edition"));
            Assert.That(summary.ReleaseDate, Is.EqualTo(album.ReleaseDate));
            Assert.That(summary.Artist, Is.Null);
            Assert.That(summary.Releases, Is.Null);
        }

        [Test]
        public void missing_next_or_previous_album_remains_null()
        {
            Assert.That(((Album)null).ToSummaryResource(), Is.Null);
        }

        [Test]
        public void detailed_album_api_behavior_remains_the_default()
        {
            var argument = typeof(ArtistController).GetMethod(nameof(ArtistController.AllArtists)).GetParameters().Single(x => x.Name == "includeAlbumDetails");
            Assert.That(argument.DefaultValue, Is.True);
        }
    }
}
