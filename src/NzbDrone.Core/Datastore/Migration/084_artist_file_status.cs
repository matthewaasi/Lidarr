using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(084)]
    public class artist_file_status : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("ArtistFileStatuses")
                .WithColumn("ArtistId").AsInt32().NotNullable()
                .WithColumn("Operation").AsInt32().NotNullable()
                .WithColumn("State").AsString().NotNullable()
                .WithColumn("Settings").AsString(int.MaxValue).NotNullable()
                .WithColumn("Pending").AsInt32().NotNullable()
                .WithColumn("LastChecked").AsDateTimeOffset().Nullable()
                .WithColumn("LastApplied").AsDateTimeOffset().Nullable();

            Create.Index().OnTable("ArtistFileStatuses")
                .OnColumn("ArtistId").Ascending()
                .OnColumn("Operation").Ascending()
                .WithOptions().Unique();
        }
    }
}
