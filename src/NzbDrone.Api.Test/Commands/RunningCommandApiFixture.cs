using System;
using System.Collections.Generic;
using Lidarr.Api.V1.Commands;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.Composition;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.SignalR;

namespace NzbDrone.Api.Test.Commands
{
    [TestFixture]
    public class RunningCommandApiFixture
    {
        [Test]
        public void delete_returns_running_cancellation_state_instead_of_removing_the_row()
        {
            var repository = new Mock<ICommandRepository>();
            repository.Setup(x => x.Insert(It.IsAny<CommandModel>())).Returns((CommandModel command) =>
            {
                command.Id = 1;
                return command;
            });
            var manager = new CommandQueueManager(repository.Object, Mock.Of<IServiceFactory>(), new KnownTypes(), LogManager.GetCurrentClassLogger());
            var model = manager.Push(new AlbumSearchCommand(new List<int> { 1 }));
            model.Status = CommandStatus.Started;
            model.StartedAt = DateTime.UtcNow;
            Assert.That(model.ToResource().CanCancel, Is.True);
            var controller = new CommandController(manager, Mock.Of<IBroadcastSignalRMessage>(), new KnownTypes());
            var response = controller.CancelCommand(model.Id);
            Assert.That(response.Status, Is.EqualTo(CommandStatus.Started));
            Assert.That(response.CancellationRequested, Is.True);
            Assert.That(response.CanCancel, Is.False);
            manager.MarkCancelled(model);
            Assert.That(model.ToResource().Status, Is.EqualTo(CommandStatus.Cancelled));
            Assert.That(model.ToResource().CancellationRequested, Is.False);
        }

        [Test]
        public void unsupported_running_command_exposes_disabled_cancel_control()
        {
            var model = new CommandModel { Id = 1, Name = "RefreshMonitoredDownloads", Body = new NzbDrone.Core.Download.RefreshMonitoredDownloadsCommand(), Status = CommandStatus.Started };
            Assert.That(model.ToResource().CanCancel, Is.False);
            Assert.That(model.ToResource().CancellationRequested, Is.False);
        }
    }
}
