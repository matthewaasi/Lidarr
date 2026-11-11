using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(082)]
    public class native_singles_review : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("SinglesDecisions")
                .WithColumn("AlbumId").AsInt32().Unique()
                .WithColumn("ArtistId").AsInt32().Indexed()
                .WithColumn("Status").AsString().Indexed()
                .WithColumn("AutoUnmonitored").AsBoolean()
                .WithColumn("Evidence").AsString(int.MaxValue)
                .WithColumn("Updated").AsDateTimeOffset();
        }
    }
}
