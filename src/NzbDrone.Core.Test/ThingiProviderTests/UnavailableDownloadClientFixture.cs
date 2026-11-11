using System.Linq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ThingiProviderTests
{
    [TestFixture]
    public class UnavailableDownloadClientFixture : DbTest<DownloadClientRepository, DownloadClientDefinition>
    {
        [Test]
        public void repeated_polling_should_warn_once_and_preserve_unknown_provider_data()
        {
            var definition = new DownloadClientDefinition
            {
                Name = "Old Deemix",
                Implementation = "Deemix",
                Settings = NullConfig.Instance,
                ConfigContract = "DeemixSettings",
                Enable = true
            };
            Subject.Insert(definition);

            Assert.That(Subject.All(), Is.Empty);
            Assert.That(Subject.All(), Is.Empty);
            Assert.That(Subject.All(), Is.Empty);
            var stored = Storage.All().Single();
            Assert.That(stored.Id, Is.EqualTo(definition.Id));
            Assert.That(stored.Name, Is.EqualTo("Old Deemix"));
            Assert.That(stored.ConfigContract, Is.EqualTo("DeemixSettings"));
            Assert.That(stored.Enable, Is.True);
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
