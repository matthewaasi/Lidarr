using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using NzbDrone.Core.ArtistStats;

namespace NzbDrone.Core.Test.ArtistStatsTests
{
    [TestFixture]
    public class ArtistStatisticsMergeFixture
    {
        [Test]
        public void sizes_are_joined_by_both_artist_and_album_without_changing_track_counts_or_order()
        {
            var tracks = new List<AlbumStatistics>
            {
                new AlbumStatistics { ArtistId = 1, AlbumId = 10, TrackCount = 7 },
                new AlbumStatistics { ArtistId = 2, AlbumId = 10, TrackCount = 12 },
                new AlbumStatistics { ArtistId = 1, AlbumId = 11, SizeOnDisk = 999 }
            };
            var files = new List<AlbumStatistics>
            {
                new AlbumStatistics { ArtistId = 2, AlbumId = 10, SizeOnDisk = 200 },
                new AlbumStatistics { ArtistId = 1, AlbumId = 10, SizeOnDisk = 100 }
            };
            var result = ArtistStatisticsRepository.MapResults(tracks, files);
            Assert.That(result, Is.SameAs(tracks));
            Assert.That(result.Select(x => x.SizeOnDisk), Is.EqualTo(new long[] { 100, 200, 0 }));
            Assert.That(result.Select(x => x.TrackCount), Is.EqualTo(new[] { 7, 12, 0 }));
        }

        [Test]
        public void empty_track_results_do_not_create_albums_from_files()
        {
            Assert.That(ArtistStatisticsRepository.MapResults(new List<AlbumStatistics>(), new List<AlbumStatistics> { new AlbumStatistics { AlbumId = 1 } }), Is.Empty);
        }

        [Test]
        public void duplicate_matching_file_groups_still_fail_instead_of_silently_choosing_one()
        {
            var duplicate = new AlbumStatistics { ArtistId = 1, AlbumId = 1 };
            Assert.Throws<InvalidOperationException>(() => ArtistStatisticsRepository.MapResults(new List<AlbumStatistics> { duplicate }, new List<AlbumStatistics> { duplicate, duplicate }));
        }

        [Test]
        public void large_catalog_matches_previous_join_and_reports_timings()
        {
            const int count = 20000;
            var tracks = Enumerable.Range(0, count).Select(i => new AlbumStatistics { ArtistId = i % 2211, AlbumId = i }).ToList();
            var files = tracks.Select(x => new AlbumStatistics { ArtistId = x.ArtistId, AlbumId = x.AlbumId, SizeOnDisk = x.AlbumId + 100L }).Reverse().ToList();
            var watch = Stopwatch.StartNew();
            var expected = tracks.Select(e => files.SingleOrDefault(f => f.ArtistId == e.ArtistId & f.AlbumId == e.AlbumId)?.SizeOnDisk ?? 0).ToArray();
            var previousMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var result = ArtistStatisticsRepository.MapResults(tracks, files);
            var indexedMs = watch.Elapsed.TotalMilliseconds;
            Assert.That(result.Select(x => x.SizeOnDisk), Is.EqualTo(expected));
            TestContext.Progress.WriteLine($"Statistics merge, {count:N0} albums: previous {previousMs:F1} ms; indexed {indexedMs:F1} ms. Synthetic in-memory benchmark, not live page timing.");
        }
    }
}
