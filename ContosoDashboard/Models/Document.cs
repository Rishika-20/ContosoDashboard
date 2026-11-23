using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public enum ScanStatus { Pending, Available, Quarantined }
public enum DocumentActivityType { Upload, Download, Delete, Share, MetadataChange, Replace, ScanPending, ScanAvailable, Quarantine, QuarantineRetry, Purge }
public enum DocumentActivityOutcome { Success, Failure, Denied }
public enum DocumentScanAttemptStatus { Pending, InProgress, Available, Quarantined, TransientFailure, Poisoned }

public class Document
{
    [Key] public int DocumentId { get; set; }
    [Required, MaxLength(255)] public string Title { get; set; } = string.Empty;
    [MaxLength(2000)] public string? Description { get; set; }
    [Required, MaxLength(100)] public string Category { get; set; } = "General";
    [Required] public int UploaderId { get; set; }
    public int? ProjectId { get; set; }
    public int? TaskId { get; set; }
    [Required, MaxLength(255)] public string OriginalFileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    [Required, MaxLength(255)] public string OriginalFileType { get; set; } = string.Empty;
    [Required, MaxLength(32)] public string SupportedFileType { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string StorageKey { get; set; } = string.Empty;
    public DateTime UploadDateUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedDateUtc { get; set; } = DateTime.UtcNow;
    public ScanStatus ScanStatus { get; set; } = ScanStatus.Pending;
    [MaxLength(2000)] public string? QuarantineFailureDetails { get; set; }
    public DateTime? QuarantinedUntilUtc { get; set; }

    [ForeignKey(nameof(UploaderId))] public virtual User Uploader { get; set; } = null!;
    [ForeignKey(nameof(ProjectId))] public virtual Project? Project { get; set; }
    [ForeignKey(nameof(TaskId))] public virtual TaskItem? Task { get; set; }
    public virtual ICollection<DocumentTag> Tags { get; set; } = new List<DocumentTag>();
    public virtual ICollection<DocumentShare> Shares { get; set; } = new List<DocumentShare>();
    public virtual ICollection<DocumentActivity> Activities { get; set; } = new List<DocumentActivity>();
    public virtual ICollection<DocumentScanAttempt> ScanAttempts { get; set; } = new List<DocumentScanAttempt>();
}
