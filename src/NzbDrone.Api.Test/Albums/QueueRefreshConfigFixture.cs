using System;
using System.Collections.Generic;
using Lidarr.Api.V1.Config;
using Lidarr.Api.V1.System.Tasks;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Jobs;
using NzbDrone.SignalR;

namespace NzbDrone.Api.Test.Albums
{
    [TestFixture]
    public class QueueRefreshConfigFixture
    {
        private sealed class TestController : DownloadClientConfigController
        {
            public TestController(IConfigService config)
                : base(config)
            {
            }

            public bool Accepts(int seconds)
            {
                return SharedValidator.Validate(new DownloadClientConfigResource { QueueRefreshIntervalSeconds = seconds }).IsValid;
            }
        }

        [TestCase(0, false)]
        [TestCase(-1, false)]
        [TestCase(1, true)]
        [TestCase(15, true)]
        [TestCase(30, true)]
        [TestCase(60, true)]
        [TestCase(86400, true)]
        [TestCase(86401, false)]
        public void api_validates_seconds_range(int seconds, bool valid)
        {
            Assert.That(new TestController(Mock.Of<IConfigService>()).Accepts(seconds), Is.EqualTo(valid));
        }

        [Test]
        public void settings_response_exposes_seconds()
        {
            var config = new Mock<IConfigService>();
            config.SetupGet(c => c.QueueRefreshIntervalSeconds).Returns(15);
            Assert.That(new TestController(config.Object).GetConfig().QueueRefreshIntervalSeconds, Is.EqualTo(15));
        }

        [Test]
        public void task_api_reports_fractional_minutes_and_correct_next_execution()
        {
            var last = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
            var manager = new Mock<ITaskManager>();
            manager.Setup(m => m.GetAll()).Returns(new List<ScheduledTask>
            {
                new ScheduledTask { Id = 1, TypeName = typeof(RefreshMonitoredDownloadsCommand).FullName, Interval = 1, IntervalSeconds = 15, LastExecution = last }
            });
            var resource = new TaskController(manager.Object, Mock.Of<IBroadcastSignalRMessage>()).GetResourceById(1);
            Assert.That(resource.Interval, Is.EqualTo(0.25));
            Assert.That(resource.NextExecution, Is.EqualTo(last.AddSeconds(15)));
        }
    }
}
