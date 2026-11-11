using System.Linq;
using FluentMigrator;
using FluentMigrator.Expressions;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Generators;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Test.MusicTests.Singles
{
    [TestFixture]
    [NonParallelizable]
    public class SinglesPostgresMigrationFixture
    {
        [Test]
        public void postgres_schema_should_use_unbounded_evidence_and_timezone_aware_dates()
        {
            using var provider = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r => r.AddPostgres())
                .Configure<SelectingGeneratorAccessorOptions>(options => options.GeneratorId = "Postgres").BuildServiceProvider();
            var generator = provider.GetRequiredService<IGeneratorAccessor>().Generator;
            var context = new FluentMigrator.Infrastructure.MigrationContext(new Mock<IQuerySchema>().Object, provider, null);
            var previous = MigrationContext.Current;
            try
            {
                MigrationContext.Current = new MigrationContext(MigrationType.Main);
                new native_singles_review().GetUpExpressions(context);
                var sql = generator.Generate(context.Expressions.OfType<CreateTableExpression>().Single()).ToLowerInvariant();
                Assert.That(sql, Does.Contain("\"evidence\" text"));
                Assert.That(sql, Does.Contain("\"updated\" timestamptz"));
            }
            finally
            {
                MigrationContext.Current = previous;
            }
        }
    }
}
