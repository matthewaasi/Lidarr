using System.Collections.Generic;
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
    public class AiQueueRetryFixture
    {
        [TestCase(true, TrackedDownloadState.ImportFailed, DownloadItemStatus.Completed, true)]
        [TestCase(false, TrackedDownloadState.ImportFailed, DownloadItemStatus.Completed, false)]
        [TestCase(true, TrackedDownloadState.Imported, DownloadItemStatus.Completed, false)]
        [TestCase(true, TrackedDownloadState.Ignored, DownloadItemStatus.Completed, false)]
        [TestCase(true, TrackedDownloadState.Importing, DownloadItemStatus.Completed, false)]
        [TestCase(true, TrackedDownloadState.ImportFailed, DownloadItemStatus.Downloading, false)]
        public void explicit_refresh_should_retry_only_eligible_failed_imports(bool explicitRetry, TrackedDownloadState state, DownloadItemStatus status, bool expectedCheck)
        {
            var client = new Mock<IDownloadClient>();
            var factory = new Mock<IDownloadClientFactory>();
            var tracked = new Mock<ITrackedDownloadService>();
            var completed = new Mock<ICompletedDownloadService>();
            var config = new Mock<IConfigService>();
            var item = new DownloadClientItem { DownloadId = "test", Status = status };
            var download = new TrackedDownload { DownloadItem = item, State = state, IsTrackable = true };
            client.SetupGet(c => c.Definition).Returns(new DownloadClientDefinition { Id = 1 });
            client.Setup(c => c.GetItems()).Returns(new[] { item });
            factory.Setup(f => f.DownloadHandlingEnabled(true)).Returns(new List<IDownloadClient> { client.Object });
            tracked.Setup(t => t.TrackDownload(It.IsAny<DownloadClientDefinition>(), item)).Returns(download);
            config.SetupGet(c => c.EnableCompletedDownloadHandling).Returns(true);
            var service = new DownloadMonitoringService(
                new Mock<IDownloadClientStatusService>().Object,
                factory.Object,
                new Mock<IEventAggregator>().Object,
                new Mock<IManageCommandQueue>().Object,
                config.Object,
                new Mock<IFailedDownloadService>().Object,
                completed.Object,
                tracked.Object,
                LogManager.GetLogger("queue-test"));

            service.Execute(new RefreshMonitoredDownloadsCommand { RetryAiMatching = explicitRetry });

            completed.Verify(c => c.Check(download), expectedCheck ? Times.Once() : Times.Never());
            if (expectedCheck)
            {
                Assert.That(download.State, Is.EqualTo(TrackedDownloadState.Downloading));
            }
            else
            {
                Assert.That(download.State, Is.EqualTo(state));
            }
        }
    }
}
