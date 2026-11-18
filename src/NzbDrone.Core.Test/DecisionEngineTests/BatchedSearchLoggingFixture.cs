using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NLog.Targets.Wrappers;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class BatchedSearchLoggingFixture
    {
        [TestCase("appFileDebug", true)]
        [TestCase("appFileTrace", true)]
        [TestCase("appFileInfo", false)]
        public void production_file_target_retains_messages_cleansing_and_flush(string name, bool asynchronous)
        {
            var folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "batched-logs", Guid.NewGuid().ToString("N"));
            var appFolder = new Mock<IAppFolderInfo>();
            appFolder.SetupGet(x => x.AppDataFolder).Returns(folder);
            var factory = typeof(NzbDroneLogger).GetMethod("CreateAppFileTarget", BindingFlags.Static | BindingFlags.NonPublic);
            var target = (Target)factory.Invoke(null, new object[] { appFolder.Object, name, "test.txt", 3 });
            Assert.That(target is AsyncTargetWrapper, Is.EqualTo(asynchronous));
            var file = target as CleansingFileTarget;
            if (target is AsyncTargetWrapper wrapper)
            {
                Assert.That(wrapper.QueueLimit, Is.EqualTo(10000));
                Assert.That(wrapper.OverflowAction, Is.EqualTo(AsyncTargetWrapperOverflowAction.Block));
                file = wrapper.WrappedTarget as CleansingFileTarget;
            }

            Assert.That(file, Is.Not.Null);
            file.ArchiveAboveSize = 4096;
            file.MaxArchiveFiles = 100;
            var config = new LoggingConfiguration();
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, target);
            using (var logFactory = new LogFactory())
            {
                logFactory.Configuration = config;
                var logger = logFactory.GetLogger("replay");
                Parallel.For(0, 1200, i => logger.Debug("entry={0} https://example.test/?apikey=secretvalue", i));
                logFactory.Flush(TimeSpan.FromSeconds(10));

                // A completed large batch may exceed the archive threshold; the next
                // write must rotate it without losing the already-flushed entries.
                logger.Debug("entry=1200 https://example.test/?apikey=secretvalue");
                logFactory.Flush(TimeSpan.FromSeconds(10));
            }

            var logFiles = Directory.GetFiles(Path.Combine(folder, "logs"), "*.txt");
            Assert.That(logFiles.Length, Is.GreaterThan(1));
            var lines = logFiles.SelectMany(File.ReadAllLines).ToArray();
            Assert.That(lines.Length, Is.EqualTo(1201));
            Assert.That(lines.Any(x => x.Contains("secretvalue")), Is.False);
            Assert.That(lines.Select(x => x.Split("entry=")[1].Split(' ')[0]).Distinct().Count(), Is.EqualTo(1201));
        }
    }
}
