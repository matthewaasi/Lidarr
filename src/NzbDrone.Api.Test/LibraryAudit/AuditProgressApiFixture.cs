using System.Text.Json;
using Lidarr.Api.V1.Commands;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MediaFiles.LibraryAudit;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Api.Test.LibraryAudit
{
    [TestFixture]
    public class AuditProgressApiFixture
    {
        public class ProgressCommand : Command
        {
            protected override object GetProgress() => new AuditScanProgress("inspection", 123, 200, 1, 3, "Inspecting files");
        }

        [Test]
        public void command_resource_exposes_runtime_progress_as_structured_camel_case_json()
        {
            var command = new ProgressCommand();
            var model = new CommandModel { Id = 1, Name = command.Name, Body = command, Status = CommandStatus.Started };
            using var json = JsonDocument.Parse(STJson.ToJson(model.ToResource()));
            var progress = json.RootElement.GetProperty("body").GetProperty("progress");
            Assert.That(progress.GetProperty("phase").GetString(), Is.EqualTo("inspection"));
            Assert.That(progress.GetProperty("processed").GetInt32(), Is.EqualTo(123));
            Assert.That(progress.GetProperty("total").GetInt32(), Is.EqualTo(200));
            Assert.That(progress.GetProperty("percentage").GetDouble(), Is.EqualTo(61.5));
            Assert.That(progress.GetProperty("stage").GetInt32(), Is.EqualTo(1));
        }

        [Test]
        public void queued_audit_does_not_invent_percentage_before_totals_are_known()
        {
            using var json = JsonDocument.Parse(STJson.ToJson(new ScanLibraryAuditCommand()));
            var progress = json.RootElement.GetProperty("progress");
            Assert.That(progress.GetProperty("phase").GetString(), Is.EqualTo("queued"));
            Assert.That(progress.TryGetProperty("percentage", out _), Is.False);
        }
    }
}
