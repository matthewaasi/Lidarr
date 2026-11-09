using System.Collections.Generic;
using System.IO.Abstractions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.TrackImport;
using NzbDrone.Core.MediaFiles.TrackImport.Manual;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Identification
{
    [TestFixture]
    public class AiManualImportPreviewFixture : CoreTest<ManualImportService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IMakeImportDecision>()
                .Setup(m => m.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.IsAny<ImportDecisionMakerConfig>()))
                .Returns(new List<ImportDecision<LocalTrack>>());
        }

        [TestCase(true)]
        [TestCase(false)]
        public void opening_manual_import_for_folder_or_file_must_only_read_saved_results(bool folder)
        {
            const string path = @"C:\Test\Album";
            var file = new Mock<IFileInfo>();
            file.SetupGet(f => f.FullName).Returns(path);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(path)).Returns(folder);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(path)).Returns(!folder);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFileInfo(path)).Returns(file.Object);
            Mocker.GetMock<IDiskScanService>().Setup(d => d.GetAudioFiles(path, true)).Returns(new[] { file.Object });

            Subject.GetMediaFiles(path, null, null, FilterFilesType.None, false);

            Mocker.GetMock<IMakeImportDecision>().Verify(
                m => m.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.Is<ImportDecisionMakerConfig>(c => c.UseCachedIdentificationOnly)),
                Times.Once());
        }

        [Test]
        public void explicit_map_tracks_must_still_request_fresh_identification()
        {
            var file = new Mock<IFileInfo>();
            file.SetupGet(f => f.FullName).Returns(@"C:\Test\track.flac");
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFileInfo(It.IsAny<string>())).Returns(file.Object);

            Subject.UpdateItems(new List<ManualImportItem> { new ManualImportItem { Path = file.Object.FullName } });

            Mocker.GetMock<IMakeImportDecision>().Verify(
                m => m.GetImportDecisions(It.IsAny<List<IFileInfo>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerInfo>(), It.Is<ImportDecisionMakerConfig>(c => !c.UseCachedIdentificationOnly)),
                Times.Once());
        }
    }
}
