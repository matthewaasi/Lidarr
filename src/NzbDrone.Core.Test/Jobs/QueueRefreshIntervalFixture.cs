using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Test.Jobs
{
    [TestFixture]
    public class QueueRefreshIntervalFixture
    {
        [TestCase(null, null, 60)]
        [TestCase("5", null, 300)]
        [TestCase("1440", null, 86400)]
        [TestCase("5", "15", 15)]
        public void existing_minutes_are_converted_once_without_overriding_saved_seconds(string minutes, string seconds, int expected)
        {
            var values = new List<Config>();
            if (minutes != null)
            {
                values.Add(new Config { Key = "queuerefreshinterval", Value = minutes });
            }

            if (seconds != null)
            {
                values.Add(new Config { Key = "queuerefreshintervalseconds", Value = seconds });
            }

            var repository = new Mock<IConfigRepository>();
            repository.Setup(r => r.All()).Returns(values);
            var config = new ConfigService(repository.Object, Mock.Of<IEventAggregator>(), LogManager.GetCurrentClassLogger());
            Assert.That(config.QueueRefreshIntervalSeconds, Is.EqualTo(expected));
            repository.Verify(r => r.Upsert(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [TestCase(1)]
        [TestCase(15)]
        [TestCase(30)]
        [TestCase(90)]
        [TestCase(86400)]
        public void startup_uses_seconds_for_queue_and_minutes_for_other_tasks(int seconds)
        {
            var last = DateTime.UtcNow.AddSeconds(-seconds - 2);
            var queue = new ScheduledTask { Id = 1, TypeName = typeof(RefreshMonitoredDownloadsCommand).FullName, Interval = 5, LastExecution = last };
            var repository = new Mock<IScheduledTaskRepository>();
            repository.Setup(r => r.All()).Returns(new List<ScheduledTask> { queue });
            var config = new Mock<IConfigService>();
            config.SetupGet(c => c.QueueRefreshIntervalSeconds).Returns(seconds);
            var manager = new TaskManager(repository.Object, config.Object, new CacheManager(), LogManager.GetCurrentClassLogger());
            manager.Handle((ApplicationStartedEvent)null);

            Assert.That(manager.GetNextExecution(typeof(RefreshMonitoredDownloadsCommand)), Is.EqualTo(last.AddSeconds(seconds)));
            Assert.That(manager.GetPending(), Does.Contain(queue));
            Assert.That(manager.GetAll().Single(t => t.TypeName.EndsWith("MessagingCleanupCommand", StringComparison.Ordinal)).GetInterval(), Is.EqualTo(TimeSpan.FromMinutes(5)));

            queue.LastExecution = DateTime.UtcNow;
            Assert.That(manager.GetPending(), Does.Not.Contain(queue));
        }

        [Test]
        public void saving_interval_updates_cached_schedule_immediately()
        {
            var repository = new Mock<IScheduledTaskRepository>();
            repository.Setup(r => r.All()).Returns(new List<ScheduledTask>());
            var config = new Mock<IConfigService>();
            config.SetupGet(c => c.QueueRefreshIntervalSeconds).Returns(300);
            var manager = new TaskManager(repository.Object, config.Object, new CacheManager(), LogManager.GetCurrentClassLogger());
            manager.Handle((ApplicationStartedEvent)null);
            var tasks = manager.GetAll();
            repository.Setup(r => r.GetDefinition(It.IsAny<Type>())).Returns((Type type) => tasks.Single(t => t.TypeName == type.FullName));
            var queue = tasks.Single(t => t.TypeName == typeof(RefreshMonitoredDownloadsCommand).FullName);
            queue.LastExecution = DateTime.UtcNow.AddSeconds(-40);
            Assert.That(manager.GetPending(), Does.Not.Contain(queue));

            config.SetupGet(c => c.QueueRefreshIntervalSeconds).Returns(15);
            manager.HandleAsync(null);
            Assert.That(manager.GetNextExecution(typeof(RefreshMonitoredDownloadsCommand)), Is.EqualTo(queue.LastExecution.AddSeconds(15)));
            Assert.That(manager.GetPending(), Does.Contain(queue));
        }
    }
}
