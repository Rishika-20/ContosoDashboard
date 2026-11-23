using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class DocumentScanAttempt
{
    [Key] public long DocumentScanAttemptId { get; set; }
    [Required] public int DocumentId { get; set; }
    [Required] public Guid CorrelationId { get; set; }
    [Required, MaxLength(500)] public string StorageKey { get; set; } = string.Empty;
    [Required] public DocumentScanAttemptStatus Status { get; set; } = DocumentScanAttemptStatus.Pending;
    public int AttemptNumber { get; set; } = 1;
    public DateTime EnqueuedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    [MaxLength(2000)] public string? FailureDetails { get; set; }
    [ForeignKey(nameof(DocumentId))] public virtual Document Document { get; set; } = null!;
}
