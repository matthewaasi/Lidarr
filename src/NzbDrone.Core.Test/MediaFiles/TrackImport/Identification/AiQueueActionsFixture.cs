using System.Collections.Generic;
using System.Linq;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Identification
{
    [TestFixture]
    public class AiQueueActionsFixture
    {
        private Mock<IDownloadClient> _client;
        private Mock<IDownloadClientFactory> _factory;
        private Mock<ITrackedDownloadService> _tracked;
        private Mock<ICompletedDownloadService> _completed;
        private Mock<IManageCommandQueue> _commands;
        private Mock<IConfigService> _config;
        private List<TrackedDownload> _downloads;
        private DownloadMonitoringService _service;

        [SetUp]
        public void Setup()
        {
            _client = new Mock<IDownloadClient>();
            _factory = new Mock<IDownloadClientFactory>();
            _tracked = new Mock<ITrackedDownloadService>();
            _completed = new Mock<ICompletedDownloadService>();
            _commands = new Mock<IManageCommandQueue>();
            _config = new Mock<IConfigService>();
            _downloads = new List<TrackedDownload>();
            _client.SetupGet(c => c.Definition).Returns(new DownloadClientDefinition { Id = 1 });
            _client.Setup(c => c.GetItems()).Returns(() => _downloads.Select(d => d.DownloadItem).ToList());
            _factory.Setup(f => f.DownloadHandlingEnabled(true)).Returns(new List<IDownloadClient> { _client.Object });
            _tracked.Setup(t => t.GetTrackedDownloads()).Returns(() => _downloads);
            _tracked.Setup(t => t.TrackDownload(It.IsAny<DownloadClientDefinition>(), It.IsAny<DownloadClientItem>()))
                .Returns((DownloadClientDefinition definition, DownloadClientItem item) => _downloads.Single(d => d.DownloadItem == item));
            _completed.Setup(c => c.Check(It.IsAny<TrackedDownload>())).Callback<TrackedDownload>(d => d.State = TrackedDownloadState.ImportPending);
            _config.SetupGet(c => c.EnableCompletedDownloadHandling).Returns(true);
            _service = new DownloadMonitoringService(
                new Mock<IDownloadClientStatusService>().Object,
                _factory.Object,
                new Mock<IEventAggregator>().Object,
                _commands.Object,
                _config.Object,
                new Mock<IFailedDownloadService>().Object,
                _completed.Object,
                _tracked.Object,
                LogManager.GetLogger("queue-actions-test"));
        }

        private TrackedDownload Add(TrackedDownloadState state)
        {
            var item = new TrackedDownload
            {
                State = state,
                IsTrackable = true,
                DownloadItem = new DownloadClientItem { DownloadId = _downloads.Count.ToString(), Status = DownloadItemStatus.Completed }
            };
            _downloads.Add(item);
            return item;
        }

        [Test]
        public void queue_refresh_must_only_update_list_without_import_processing()
        {
            foreach (var state in new[] { TrackedDownloadState.Downloading, TrackedDownloadState.ImportBlocked, TrackedDownloadState.ImportFailed, TrackedDownloadState.ImportPending })
            {
                Add(state);
            }

            _service.Execute(new RefreshMonitoredDownloadsCommand { QueueOnly = true });

            _completed.VerifyNoOtherCalls();
            _commands.VerifyNoOtherCalls();
            Assert.That(_downloads[2].State, Is.EqualTo(TrackedDownloadState.ImportFailed));
        }

        [Test]
        public void retry_ai_must_process_all_held_completed_downloads_without_queueing_a_second_worker()
        {
            var first = Add(TrackedDownloadState.ImportFailed);
            var second = Add(TrackedDownloadState.ImportFailed);
            var imported = Add(TrackedDownloadState.Imported);
            var ignored = Add(TrackedDownloadState.Ignored);
            var inProgress = Add(TrackedDownloadState.Importing);

            _service.Execute(new RetryAiMatchesCommand());

            _completed.Verify(c => c.Import(first), Times.Once());
            _completed.Verify(c => c.Import(second), Times.Once());
            _completed.Verify(c => c.Import(imported), Times.Never());
            _completed.Verify(c => c.Import(ignored), Times.Never());
            _completed.Verify(c => c.Import(inProgress), Times.Never());
            _commands.VerifyNoOtherCalls();
        }

        [Test]
        public void disabled_completed_download_handling_must_leave_retry_states_unchanged()
        {
            var item = Add(TrackedDownloadState.ImportFailed);
            _config.SetupGet(c => c.EnableCompletedDownloadHandling).Returns(false);

            _service.Execute(new RetryAiMatchesCommand());

            Assert.That(item.State, Is.EqualTo(TrackedDownloadState.ImportFailed));
            _completed.VerifyNoOtherCalls();
            _factory.VerifyNoOtherCalls();
        }

        [Test]
        public void scheduled_polling_keeps_normal_download_processing_without_rearming_review_holds()
        {
            var fresh = Add(TrackedDownloadState.Downloading);
            var held = Add(TrackedDownloadState.ImportFailed);

            _service.Execute(new RefreshMonitoredDownloadsCommand());

            _completed.Verify(c => c.Check(fresh), Times.Once());
            _completed.Verify(c => c.Check(held), Times.Never());
            Assert.That(held.State, Is.EqualTo(TrackedDownloadState.ImportFailed));
        }
    }
}
