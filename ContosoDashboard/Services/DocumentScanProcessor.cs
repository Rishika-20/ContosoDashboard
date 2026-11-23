using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Services;

public sealed class DocumentScanProcessor
{
    private readonly ApplicationDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly IMalwareScanner _scanner;
    public DocumentScanProcessor(ApplicationDbContext db, IFileStorageService storage, IMalwareScanner scanner)
    { _db = db; _storage = storage; _scanner = scanner; }

    public async Task<ScanResultStatus> ProcessAsync(DocumentQueueMessage message, CancellationToken cancellationToken = default)
    {
        var document = await _db.Documents.Include(d => d.ScanAttempts).FirstOrDefaultAsync(d => d.DocumentId == message.DocumentId, cancellationToken);
        if (document is null) return ScanResultStatus.TransientFailure;
        if (document.ScanStatus is ScanStatus.Available or ScanStatus.Quarantined) return document.ScanStatus == ScanStatus.Available ? ScanResultStatus.Available : ScanResultStatus.Quarantined;
        if (!DocumentValidation.IsValidStorageKey(message.StorageKey) ||
            !string.Equals(document.StorageKey, message.StorageKey, StringComparison.Ordinal))
            return ScanResultStatus.TransientFailure;
        var attempt = document.ScanAttempts.FirstOrDefault(a => a.CorrelationId == message.CorrelationId);
        if (attempt is null)
        {
            attempt = new DocumentScanAttempt { DocumentId = document.DocumentId, CorrelationId = message.CorrelationId,
                StorageKey = message.StorageKey, AttemptNumber = message.Attempt, Status = DocumentScanAttemptStatus.InProgress };
            _db.DocumentScanAttempts.Add(attempt);
        }
        else if (attempt.Status is DocumentScanAttemptStatus.Available or DocumentScanAttemptStatus.Quarantined)
            return attempt.Status == DocumentScanAttemptStatus.Available ? ScanResultStatus.Available : ScanResultStatus.Quarantined;
        attempt.Status = DocumentScanAttemptStatus.InProgress;
        await _db.SaveChangesAsync(cancellationToken);
        await using var content = await _storage.OpenReadAsync(message.StorageKey, cancellationToken);
        if (content is null)
        {
            attempt.Status = DocumentScanAttemptStatus.TransientFailure;
            attempt.FailureDetails = "Stored file unavailable";
            await _db.SaveChangesAsync(cancellationToken);
            return ScanResultStatus.TransientFailure;
        }
        var result = await _scanner.ScanAsync(message.StorageKey, content, cancellationToken);
        if (result.Status == ScanResultStatus.Available)
        {
            document.ScanStatus = ScanStatus.Available;
            document.QuarantineFailureDetails = null;
            document.QuarantinedUntilUtc = null;
            attempt.Status = DocumentScanAttemptStatus.Available;
            attempt.CompletedAtUtc = DateTime.UtcNow;
            _db.DocumentActivities.Add(new DocumentActivity { DocumentId = document.DocumentId, ActorUserId = document.UploaderId,
                ActivityType = DocumentActivityType.ScanAvailable, Outcome = DocumentActivityOutcome.Success });
        }
        else if (result.Status == ScanResultStatus.Quarantined)
        {
            document.ScanStatus = ScanStatus.Quarantined;
            document.QuarantineFailureDetails = string.IsNullOrWhiteSpace(result.Details) ? "Confirmed scan failure" : result.Details[..Math.Min(result.Details.Length, 2000)];
            document.QuarantinedUntilUtc = DateTime.UtcNow.AddDays(30);
            attempt.Status = DocumentScanAttemptStatus.Quarantined;
            attempt.CompletedAtUtc = DateTime.UtcNow;
            attempt.FailureDetails = document.QuarantineFailureDetails;
            _db.DocumentActivities.Add(new DocumentActivity { DocumentId = document.DocumentId, ActorUserId = document.UploaderId,
                ActivityType = DocumentActivityType.Quarantine, Outcome = DocumentActivityOutcome.Success });
        }
        else
        {
            attempt.Status = DocumentScanAttemptStatus.TransientFailure;
            attempt.FailureDetails = "Scanner infrastructure failure";
        }
        await _db.SaveChangesAsync(cancellationToken);
        return result.Status;
    }

    public async Task MarkPoisonedAsync(DocumentQueueMessage message, CancellationToken cancellationToken = default)
    {
        var attempt = await _db.DocumentScanAttempts.FirstOrDefaultAsync(
            a => a.DocumentId == message.DocumentId && a.CorrelationId == message.CorrelationId, cancellationToken);
        if (attempt is null) return;
        if (attempt.Status is DocumentScanAttemptStatus.Available or DocumentScanAttemptStatus.Quarantined) return;
        attempt.Status = DocumentScanAttemptStatus.Poisoned;
        attempt.AttemptNumber = Math.Max(attempt.AttemptNumber, message.Attempt);
        attempt.CompletedAtUtc = DateTime.UtcNow;
        attempt.FailureDetails = "Queue retry limit reached";
        await _db.SaveChangesAsync(cancellationToken);
    }
}
