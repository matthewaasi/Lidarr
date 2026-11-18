using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class SearchIndexFixture
    {
        private static Artist Artist(int id, string name, params string[] aliases)
        {
            return new Artist { Id = id, CleanName = name.CleanArtistName(), Metadata = new ArtistMetadata { Name = name, Aliases = aliases.ToList() } };
        }

        [Test]
        public void exact_indexes_preserve_duplicates_case_and_album_or_semantics()
        {
            var artists = new ArtistSearchIndex(new List<Artist> { Artist(1, "AC/DC"), Artist(2, "AC DC") });
            Assert.Throws<MultipleArtistsFoundException>(() => artists.FindExact("acdc"));
            Assert.That(artists.FindExact("ACDC"), Is.Null);
            var albums = new List<Album>
            {
                new Album { Id = 1, Title = "Greatest Hits", CleanTitle = "greatesthits" },
                new Album { Id = 2, Title = "Greatest Hits", CleanTitle = "greatesthits" },
                new Album { Id = 3, Title = "!!!", CleanTitle = "!!!" },
                new Album { Id = 4, Title = "Unique", CleanTitle = "unique" }
            };
            var index = new AlbumSearchIndex(albums);
            foreach (var title in new[] { "Greatest Hits", "GREATEST HITS", "!!!", "Unique", "Missing" })
            {
                var clean = title.CleanArtistName();
                if (string.IsNullOrEmpty(clean))
                {
                    clean = title;
                }

                Assert.That(index.Find(title)?.Id, Is.EqualTo(albums.Where(x => x.CleanTitle == clean || x.Title == title).ExclusiveOrDefault()?.Id));
            }
        }

        [Test]
        public void indexed_fuzzy_matching_matches_legacy_on_captured_or_representative_queries()
        {
            var artists = Enumerable.Range(1, 128).Select(i => Artist(i, $"Library Artist {i}", $"Alias {i}")).ToList();
            artists.AddRange(new[] { Artist(200, "Natalie Cole", "Nat Cole"), Artist(201, "Sheryl Crow"), Artist(202, "Guided by Voices"), Artist(203, "The Black Keys"), Artist(204, "The Black Eyed Peas"), Artist(205, "Beyoncé", "Beyonce Knowles") });
            var repository = new Mock<IArtistRepository>();
            repository.Setup(x => x.All()).Returns(artists);
            var service = new ArtistService(repository.Object, Mock.Of<IEventAggregator>(), Mock.Of<IBuildArtistPaths>(), new CacheManager(), LogManager.GetCurrentClassLogger());
            var queries = new List<string> { "Natalie Cole", "Nat Cole", "Natalie Cole VA", "Natalie Cole Now Compilation CD's", "Guided by Voices Guided by Voices", "The Black Peas", "Black Keys", "Beyoncé", "Beyonce Knowles", "", "Library Artist 12" };
            var captured = Environment.GetEnvironmentVariable("LIDARR_REPLAY_ARTISTS");
            if (!string.IsNullOrEmpty(captured))
            {
                queries.AddRange(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(captured)));
            }

            queries = queries.Distinct().ToList();
            var timer = Stopwatch.StartNew();
            var expected = queries.Select(x => service.FindByNameInexact(x)?.Id).ToArray();
            var legacySeconds = timer.Elapsed.TotalSeconds;
            timer.Restart();
            using (new ReleaseEvaluationContext())
            {
                var actual = queries.Select(x => service.FindByNameInexact(x)?.Id).ToArray();
                Assert.That(actual, Is.EqualTo(expected));
            }

            TestContext.Out.WriteLine($"ARTIST_REPLAY queries={queries.Count} catalog={artists.Count} legacy_seconds={legacySeconds:F3} indexed_seconds={timer.Elapsed.TotalSeconds:F3}; real scoring, fixture catalog, no production database");
        }

        [Test]
        public void criteria_regex_cache_preserves_every_parsed_field_on_captured_titles()
        {
            var titles = new List<string> { "Natalie Cole - Natalie (1976) FLAC", "Natalie Cole Now Compilation CD's - Complete Collection (1989) 08 - I Live for Your Love FLAC", "unknown", "Guided by Voices Guided by Voices - Alien Lanes MP3 320kbps" };
            var captured = Environment.GetEnvironmentVariable("LIDARR_REPLAY_TITLES");
            if (!string.IsNullOrEmpty(captured))
            {
                titles.AddRange(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(captured)));
            }

            titles = titles.Distinct().ToList();
            var artist = Artist(1, "Natalie Cole");
            var albums = new List<Album> { new Album { Title = "Natalie" } };
            string Parse(string title) => JsonSerializer.Serialize(Parser.Parser.ParseAlbumTitleWithSearchCriteria(title, artist, albums));
            var timer = Stopwatch.StartNew();
            var expected = titles.Select(Parse).ToArray();
            var legacySeconds = timer.Elapsed.TotalSeconds;
            timer.Restart();
            using (new ReleaseEvaluationContext())
            {
                Assert.That(titles.Select(Parse).ToArray(), Is.EqualTo(expected));
            }

            TestContext.Out.WriteLine($"PARSER_REPLAY titles={titles.Count} legacy_seconds={legacySeconds:F3} cached_seconds={timer.Elapsed.TotalSeconds:F3}; production parser, every serialized parsed field compared");
        }
    }
}
