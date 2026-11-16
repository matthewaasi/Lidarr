using System;
using System.Collections.Generic;
using System.Threading;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.Composition;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Test.Messaging.Commands
{
    [TestFixture]
    [NonParallelizable]
    public class RunningCancellationFixture
    {
        public class CancellableProbeCommand : Command
        {
            public int WorkId { get; set; }
            public override bool SupportsCancellation => true;
            public override bool SendUpdatesToClient => true;
        }

        private CommandQueueManager _manager;
        private Mock<ICommandRepository> _repository;

        [SetUp]
        public void Setup()
        {
            var records = new Dictionary<int, CommandModel>();
            _repository = new Mock<ICommandRepository>();
            _repository.Setup(x => x.Insert(It.IsAny<CommandModel>())).Returns((CommandModel model) =>
            {
                model.Id = records.Count + 1;
                records.Add(model.Id, model);
                return model;
            });
            _repository.Setup(x => x.Get(It.IsAny<int>())).Returns((int id) => records[id]);
            _manager = new CommandQueueManager(_repository.Object, Mock.Of<IServiceFactory>(), new KnownTypes(), LogManager.GetCurrentClassLogger());
        }

        private CommandModel Started(Command body)
        {
            var model = _manager.Push(body);
            model.Status = CommandStatus.Started;
            model.StartedAt = DateTime.UtcNow;
            return model;
        }

        [Test]
        public void request_keeps_task_running_until_acknowledged_and_does_not_cancel_other_tasks()
        {
            var first = Started(new CancellableProbeCommand { WorkId = 1 });
            var other = Started(new CancellableProbeCommand { WorkId = 2 });
            _manager.Cancel(first.Id);
            Assert.That(first.Status, Is.EqualTo(CommandStatus.Started));
            Assert.That(first.Body.CancellationRequested, Is.True);
            Assert.That(other.Body.CancellationRequested, Is.False);
            Assert.That(_manager.GetStarted(), Does.Contain(first));
            _repository.Verify(x => x.End(It.IsAny<CommandModel>()), Times.Never);
            _manager.Complete(first, "Finished");
            Assert.That(first.Status, Is.EqualTo(CommandStatus.Cancelled));
            Assert.That(first.Result, Is.EqualTo(CommandResult.Unsuccessful));
            Assert.That(first.EndedAt, Is.Not.Null);
            Assert.That(other.Status, Is.EqualTo(CommandStatus.Started));
        }

        [Test]
        public void progress_updates_do_not_break_duplicate_command_detection()
        {
            var body = new NzbDrone.Core.MediaFiles.LibraryAudit.ScanLibraryAuditCommand();
            var first = _manager.Push(body);
            body.ReportProgress("inspection", 75, 100);
            var repeated = _manager.Push(new NzbDrone.Core.MediaFiles.LibraryAudit.ScanLibraryAuditCommand());
            Assert.That(repeated, Is.SameAs(first));
        }

        [Test]
        public void queued_cancellation_is_persisted_without_a_start_time()
        {
            var model = _manager.Push(new CommandA());
            _manager.Cancel(model.Id);
            Assert.That(model.Status, Is.EqualTo(CommandStatus.Cancelled));
            Assert.That(_manager.Get(model.Id).Status, Is.EqualTo(CommandStatus.Cancelled));
            _repository.Verify(x => x.End(model), Times.Once);
        }

        [Test]
        public void unsupported_running_task_is_rejected_without_signalling_cancellation()
        {
            var model = Started(new CommandA());
            Assert.Throws<NzbDroneClientException>(() => _manager.Cancel(model.Id));
            Assert.That(model.Body.CancellationRequested, Is.False);
            Assert.That(model.Status, Is.EqualTo(CommandStatus.Started));
        }

        [Test]
        public void finalizing_task_cannot_be_interrupted_mid_commit()
        {
            var model = Started(new CancellableProbeCommand());
            model.Body.BeginFinalization();
            Assert.Throws<NzbDroneClientException>(() => _manager.Cancel(model.Id));
            Assert.That(model.Body.CancellationRequested, Is.False);
            _manager.Complete(model, "Saved");
            Assert.That(model.Status, Is.EqualTo(CommandStatus.Completed));
        }

        [Test]
        public void cancelled_tasks_serialize_without_runtime_token_state()
        {
            var model = Started(new CancellableProbeCommand());
            _manager.Cancel(model.Id);
            Assert.That(STJson.ToJson(model.Body), Does.Not.Contain("cancellationToken"));
            Assert.That(Json.ToJson(model.Body), Does.Not.Contain("cancellationToken"));
        }

        [Test]
        public void executor_marks_cooperative_cancellation_as_cancelled_and_not_failed()
        {
            using var started = new ManualResetEventSlim();
            using var ended = new ManualResetEventSlim();
            var handler = new Mock<IExecute<CancellableProbeCommand>>();
            handler.Setup(x => x.Execute(It.IsAny<CancellableProbeCommand>())).Callback((CancellableProbeCommand body) =>
            {
                started.Set();
                Assert.That(body.CancellationToken.WaitHandle.WaitOne(5000), Is.True);
                body.CancellationToken.ThrowIfCancellationRequested();
            });
            var factory = new Mock<IServiceFactory>();
            factory.Setup(x => x.Build(typeof(IExecute<CancellableProbeCommand>))).Returns(handler.Object);
            var events = new Mock<IEventAggregator>();
            events.Setup(x => x.PublishEvent(It.IsAny<CommandExecutedEvent>())).Callback(() => ended.Set());
            var executor = new CommandExecutor(factory.Object, _manager, events.Object, LogManager.GetCurrentClassLogger());
            var model = _manager.Push(new CancellableProbeCommand());
            executor.Handle(new ApplicationStartedEvent());
            try
            {
                Assert.That(started.Wait(5000), Is.True);
                _manager.Cancel(model.Id);
                Assert.That(ended.Wait(5000), Is.True);
                Assert.That(model.Status, Is.EqualTo(CommandStatus.Cancelled));
                Assert.That(model.Exception, Is.Null);
                Assert.That(_manager.GetStarted(), Does.Not.Contain(model));
            }
            finally
            {
                model.Body.RequestCancellation();
                ended.Wait(5000);
                executor.Handle(new ApplicationShutdownRequested());
            }
        }
    }
}
