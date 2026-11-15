using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaFiles.LibraryAudit
{
    public class LibraryAuditFinding : ModelBase
    {
        public string EvidenceKey { get; set; }
        public string Kind { get; set; }
        public string Status { get; set; }
        public string Evidence { get; set; }
        public DateTime Updated { get; set; }
    }

    public class AuditFindingSelection
    {
        public int Id { get; set; }
        public string EvidenceKey { get; set; }
    }

    public class AuditFileDeletion
    {
        public List<AuditFindingSelection> Findings { get; set; }
        public int[] FileIds { get; set; }
        public string PreviewKey { get; set; }
        public bool ConfirmAllCopies { get; set; }
    }

    public class AuditDeletionPreview
    {
        public List<AuditFile> Files { get; set; }
        public long TotalBytes { get; set; }
        public List<AuditDuplicateDeletionGroup> DuplicateGroups { get; set; }
        public string PreviewKey { get; set; }
    }

    public class AuditDuplicateDeletionGroup
    {
        public int FindingId { get; set; }
        public string EvidenceKey { get; set; }
        public List<AuditFile> Files { get; set; }
    }

    public class AuditEvidence
    {
        public string Confidence { get; set; }
        public string Reason { get; set; }
        public string SuggestedAction { get; set; }
        public List<AuditFile> Files { get; set; } = new List<AuditFile>();
        public List<AuditCandidate> Candidates { get; set; } = new List<AuditCandidate>();
    }

    public class AuditFile
    {
        public int Id { get; set; }
        public string Path { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        public string Artist { get; set; }
        public string ForeignArtistId { get; set; }
        public string ForeignAlbumId { get; set; }
        public string CatalogTitle { get; set; }
        public string TagTitle { get; set; }
        public double DurationSeconds { get; set; }
        public double CatalogDurationSeconds { get; set; }
        public string RecordingId { get; set; }
        public List<string> AudioRecordingIds { get; set; } = new List<string>();
        public string FingerprintStatus { get; set; } = "Not requested";
    }

    public class AuditCandidate
    {
        public string Title { get; set; }
        public string RecordingId { get; set; }
        public double DurationSeconds { get; set; }
    }

    public interface ILibraryAuditRepository : IBasicRepository<LibraryAuditFinding>
    {
    }

    public class LibraryAuditRepository : BasicRepository<LibraryAuditFinding>, ILibraryAuditRepository
    {
        public LibraryAuditRepository(IMainDatabase database, IEventAggregator events)
            : base(database, events)
        {
        }
    }
}
