using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(083)]
    public class library_audit : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("LibraryAuditFindings")
                .WithColumn("EvidenceKey").AsString(64).Unique()
                .WithColumn("Kind").AsString().Indexed()
                .WithColumn("Status").AsString().Indexed()
                .WithColumn("Evidence").AsString(int.MaxValue)
                .WithColumn("Updated").AsDateTimeOffset();
        }
    }
}
