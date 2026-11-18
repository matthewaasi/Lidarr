using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Music;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.ArtistServiceTests
{
    [TestFixture]

    public class FindByNameInexactFixture : CoreTest<ArtistService>
    {
        [Test]
        public void search_scope_caches_exact_misses_and_next_search_reloads()
        {
            using (new ReleaseEvaluationContext())
            {
                Subject.FindByName("Unknown Name").Should().BeNull();
                Subject.FindByName("Unknown Name").Should().BeNull();
            }

            Mocker.GetMock<IArtistRepository>().Verify(x => x.All(), Times.Once());
            Mocker.GetMock<IArtistRepository>().Verify(x => x.FindByName("unknownname"), Times.Never());
            Subject.FindByName("Unknown Name");
            Mocker.GetMock<IArtistRepository>().Verify(x => x.FindByName("unknownname"), Times.Once());
        }

        private List<Artist> _artists;

        private Artist CreateArtist(string name)
        {
            return Builder<Artist>.CreateNew()
                .With(a => a.Name = name)
                .With(a => a.CleanName = Parser.Parser.CleanArtistName(name))
                .With(a => a.ForeignArtistId = name)
                .BuildNew();
        }

        [SetUp]
        public void Setup()
        {
            _artists = new List<Artist>();
            _artists.Add(CreateArtist("The Black Eyed Peas"));
            _artists.Add(CreateArtist("The Black Keys"));

            Mocker.GetMock<IArtistRepository>()
                .Setup(s => s.All())
                .Returns(_artists);
        }

        [TestCase("The Black Eyde Peas", "The Black Eyed Peas")]
        [TestCase("Black Eyed Peas", "The Black Eyed Peas")]
        [TestCase("The Black eys", "The Black Keys")]
        [TestCase("Black Keys", "The Black Keys")]
        public void should_find_artist_in_db_by_name_inexact(string name, string expected)
        {
            var artist = Subject.FindByNameInexact(name);

            artist.Should().NotBeNull();
            artist.Name.Should().Be(expected);
        }

        [Test]
        public void should_find_artist_when_the_is_omitted_from_start()
        {
            _artists = new List<Artist>();
            _artists.Add(CreateArtist("Black Keys"));
            _artists.Add(CreateArtist("The Black Eyed Peas"));

            Mocker.GetMock<IArtistRepository>()
                .Setup(s => s.All())
                .Returns(_artists);

            Subject.FindByNameInexact("The Black Keys").Should().NotBeNull();
        }

        [TestCase("The Black Peas")]
        public void should_not_find_artist_in_db_by_ambiguous_name(string name)
        {
            var artist = Subject.FindByNameInexact(name);

            artist.Should().BeNull();
        }
    }
}
