using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class ReleaseEvaluationContextFixture
    {
        [Test]
        public void shared_lazy_entities_materialize_once_across_workers()
        {
            var calls = 0;
            var lazy = new LazyLoaded<Album, Artist>(
                (_, _) =>
                {
                    Interlocked.Increment(ref calls);
                    Thread.Sleep(10);
                    return new Artist { Id = 7 };
                },
                _ => true);
            lazy.Prepare(null, new Album());
            var artists = new Artist[100];
            Parallel.For(0, artists.Length, i => artists[i] = lazy.Value);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(artists.All(x => ReferenceEquals(x, artists[0])), Is.True);
        }

        [Test]
        public void reads_outside_search_are_not_cached()
        {
            var calls = 0;
            for (var i = 0; i < 3; i++)
            {
                ReleaseEvaluationContext.Read("test", 1, () => ++calls);
            }

            Assert.That(calls, Is.EqualTo(3));
        }

        [Test]
        public void concurrent_misses_and_null_matches_are_coalesced()
        {
            using var scope = new ReleaseEvaluationContext();
            var calls = 0;
            Parallel.For(0, 1000, _ =>
            {
                var result = ReleaseEvaluationContext.Read<object>("test", "same title", () =>
                {
                    Interlocked.Increment(ref calls);
                    return null;
                });
                Assert.That(result, Is.Null);
            });
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void nested_searches_restore_outer_scope_and_do_not_leak_to_next_search()
        {
            using (new ReleaseEvaluationContext())
            {
                Assert.That(ReleaseEvaluationContext.Read("test", 1, () => "outer"), Is.EqualTo("outer"));
                using (new ReleaseEvaluationContext())
                {
                    Assert.That(ReleaseEvaluationContext.Read("test", 1, () => "inner"), Is.EqualTo("inner"));
                }

                Assert.That(ReleaseEvaluationContext.Read("test", 1, () => "changed"), Is.EqualTo("outer"));
            }

            using (new ReleaseEvaluationContext())
            {
                Assert.That(ReleaseEvaluationContext.Read("test", 1, () => "new search"), Is.EqualTo("new search"));
            }
        }

        [Test]
        public async Task concurrent_searches_keep_separate_values()
        {
            async Task<string> Search(string value)
            {
                using var scope = new ReleaseEvaluationContext();
                await Task.Yield();
                return ReleaseEvaluationContext.Read("test", 1, () => value);
            }

            Assert.That(await Task.WhenAll(Search("first"), Search("second")), Is.EqualTo(new[] { "first", "second" }));
        }

        [Test]
        public void album_files_are_loaded_once_and_callers_receive_independent_lists()
        {
            var repository = new Mock<IMediaFileRepository>();
            repository.Setup(x => x.GetFilesByAlbum(7)).Returns(new List<TrackFile> { new TrackFile { Id = 1 } });
            var service = new MediaFileService(repository.Object, Mock.Of<IEventAggregator>(), LogManager.GetCurrentClassLogger());
            using (new ReleaseEvaluationContext())
            {
                Parallel.For(0, 1000, _ =>
                {
                    var files = service.GetFilesByAlbum(7);
                    Assert.That(files.Select(x => x.Id), Is.EqualTo(new[] { 1 }));
                    files.Clear();
                });
            }

            repository.Verify(x => x.GetFilesByAlbum(7), Times.Once());
            service.GetFilesByAlbum(7);
            repository.Verify(x => x.GetFilesByAlbum(7), Times.Exactly(2));
        }

        [Test]
        public void cache_keys_separate_operations_and_album_years()
        {
            using var scope = new ReleaseEvaluationContext();
            Assert.That(ReleaseEvaluationContext.Read("album", (1, "Title", 2000), () => 1), Is.EqualTo(1));
            Assert.That(ReleaseEvaluationContext.Read("album", (1, "Title", 2001), () => 2), Is.EqualTo(2));
            Assert.That(ReleaseEvaluationContext.Read("artist", (1, "Title", 2000), () => 3), Is.EqualTo(3));
        }
    }
}
