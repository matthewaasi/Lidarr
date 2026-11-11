using System;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.LibraryAudit;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFilesTests.LibraryAudit
{
    [TestFixture]
    public class LibraryAuditRepositoryFixture : DbTest<LibraryAuditRepository, LibraryAuditFinding>
    {
        [Test]
        public void migration_and_repository_preserve_findings_and_review_status()
        {
            var row = Subject.Insert(new LibraryAuditFinding
            {
                EvidenceKey = new string('A', 64), Kind = "version", Status = "pending", Evidence = "{}", Updated = DateTime.UtcNow
            });
            row.Status = "ignored";
            Subject.Update(row);
            Assert.That(Subject.Get(row.Id).Status, Is.EqualTo("ignored"));
            Assert.That(Subject.Get(row.Id).EvidenceKey, Is.EqualTo(new string('A', 64)));
        }
    }
}
