using NUnit.Framework;
using NzbDrone.Core.MediaFiles.TrackImport.Identification;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Identification
{
    [TestFixture]
    public class AiQueueSummaryFixture
    {
        [Test]
        public void review_mode_proposals_are_approved()
        {
            Assert.That(AiQueueSummary.Get(new[] { new AiMatchResult { Proposed = true, Status = "match" } }), Is.EqualTo("approved"));
        }

        [Test]
        public void no_match_takes_priority_over_approved_and_errors()
        {
            Assert.That(AiQueueSummary.Get(new[] { new AiMatchResult { Proposed = true }, new AiMatchResult { Status = "no_match", IsError = true }, new AiMatchResult { Status = "error" } }), Is.EqualTo("notAMatch"));
        }

        [TestCase("review")]
        [TestCase("error")]
        public void incomplete_or_failed_matches_have_issues(string status)
        {
            Assert.That(AiQueueSummary.Get(new[] { new AiMatchResult { Proposed = true }, new AiMatchResult { Status = status } }), Is.EqualTo("issuesFound"));
        }

        [Test]
        public void no_saved_assessment_is_not_approved()
        {
            Assert.That(AiQueueSummary.Get(new[] { new AiMatchResult { IsCached = true } }), Is.Null);
            Assert.That(AiQueueSummary.Get(new AiMatchResult[0]), Is.Null);
        }
    }
}
