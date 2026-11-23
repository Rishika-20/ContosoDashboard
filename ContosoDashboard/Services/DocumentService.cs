using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Services;

public sealed class DocumentService : IDocumentService
{
    private readonly ApplicationDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly IDocumentQueueService _queue;
    private readonly DocumentAuthorizationService _auth;
    private readonly ILogger<DocumentService> _logger;
    private readonly INotificationService _notifications;

    public DocumentService(ApplicationDbContext db, IFileStorageService storage, IDocumentQueueService queue,
        DocumentAuthorizationService auth, ILogger<DocumentService> logger, INotificationService notifications)
    {
        _db = db; _storage = storage; _queue = queue; _auth = auth; _logger = logger; _notifications = notifications;
    }

    private async Task AddActivityAsync(Document document, int actor, DocumentActivityType type,
        DocumentActivityOutcome outcome, string? details = null, int? targetUserId = null, string? department = null)
    {
        _db.DocumentActivities.Add(new DocumentActivity
        {
            DocumentId = document.DocumentId, ActorUserId = actor, ActivityType = type,
            ProjectId = document.ProjectId, TargetUserId = targetUserId, TargetDepartment = department,
            Outcome = outcome, Details = details
        });
    }

    public async Task<IReadOnlyList<UploadResult>> UploadAsync(int actorUserId, UploadRequest request,
        IReadOnlyList<UploadFile> files, CancellationToken cancellationToken = default)
    {
        var results = new List<UploadResult>();
        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 255 ||
            string.IsNullOrWhiteSpace(request.Category) || !DocumentValidation.Categories.Contains(request.Category))
        {
            return files.Select(f => new UploadResult(f.FileName, null, "Rejected", false, "Invalid title or category")).ToList();
        }
        if (request.Description?.Length > 2000) return files.Select(f => new UploadResult(f.FileName, null, "Rejected", false, "Description exceeds 2000 characters")).ToList();
        if (request.ProjectId.HasValue && !await _auth.CanUploadProjectAsync(actorUserId, request.ProjectId.Value))
            return files.Select(f => new UploadResult(f.FileName, null, "Rejected", false, "Not an active project member")).ToList();
        if (request.TaskId.HasValue)
        {
            var task = await _db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.TaskId == request.TaskId.Value, cancellationToken);
            if (task is null) return files.Select(f => new UploadResult(f.FileName, null, "Rejected", false, "Unknown task")).ToList();
            request = request with { ProjectId = task.ProjectId };
            if (task.ProjectId.HasValue && !await _auth.CanUploadProjectAsync(actorUserId, task.ProjectId.Value))
                return files.Select(f => new UploadResult(f.FileName, null, "Rejected", false, "Not an active project member")).ToList();
        }

        foreach (var file in files)
        {
            var fileSize = file.DeclaredSizeBytes ?? file.Content.LongLength;
            if (!DocumentValidation.TryValidate(file.FileName, file.ContentType, fileSize, out var supportedType, out var error))
            { results.Add(new UploadResult(file.FileName, null, "Rejected", false, error ?? "Invalid file")); continue; }
            if (fileSize != file.Content.LongLength)
            {
                results.Add(new UploadResult(file.FileName, null, "Rejected", false, "File size did not match uploaded content"));
                continue;
            }

            var key = DocumentValidation.GuidStorageKey(file.FileName);
            Document? document = null;
            try
            {
                await using var stream = new MemoryStream(file.Content);
                await _storage.UploadAsync(key, stream, cancellationToken);
                document = new Document
                {
                    Title = title, Description = request.Description?.Trim(), Category = request.Category,
                    UploaderId = actorUserId, ProjectId = request.ProjectId, TaskId = request.TaskId,
                    OriginalFileName = Path.GetFileName(file.FileName), FileSizeBytes = fileSize,
                    OriginalFileType = file.ContentType ?? "application/octet-stream",
                    SupportedFileType = supportedType!, StorageKey = key, ScanStatus = ScanStatus.Pending
                };
                _db.Documents.Add(document);
                await _db.SaveChangesAsync(cancellationToken);

                var tags = (request.Tags ?? Array.Empty<string>())
                    .Select(t => t.Trim()).Where(t => t.Length > 0 && t.Length <= 100)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(50)
                    .Select(t => new DocumentTag { Document = document, Name = t.ToLowerInvariant() });
                _db.DocumentTags.AddRange(tags);
                var correlation = Guid.NewGuid();
                _db.DocumentScanAttempts.Add(new DocumentScanAttempt
                {
                    DocumentId = document.DocumentId, CorrelationId = correlation, StorageKey = key,
                    Status = DocumentScanAttemptStatus.Pending, AttemptNumber = 1
                });
                await AddActivityAsync(document, actorUserId, DocumentActivityType.ScanPending, DocumentActivityOutcome.Success);
                await AddActivityAsync(document, actorUserId, DocumentActivityType.Upload, DocumentActivityOutcome.Success);
                await _db.SaveChangesAsync(cancellationToken);
                try
                {
                    await _queue.EnqueueAsync(new DocumentQueueMessage(document.DocumentId, key, 1, correlation), cancellationToken);
                }
                catch (Exception ex)
                {
                    // Queue outages must leave the file inaccessible in Pending rather than
                    // deleting a valid upload or accidentally bypassing its scan.
                    _logger.LogWarning(ex, "Scan queue unavailable for document {DocumentId}; leaving it Pending", document.DocumentId);
                }
                if (document.ProjectId.HasValue)
                {
                    try
                    {
                        var memberIds = await _db.ProjectMembers.AsNoTracking()
                            .Where(pm => pm.ProjectId == document.ProjectId.Value)
                            .Select(pm => pm.UserId).Distinct().ToListAsync(cancellationToken);
                        foreach (var memberId in memberIds.Where(id => id != actorUserId))
                        {
                            await _notifications.CreateNotificationAsync(memberId, "Project document added",
                                $"A document was added to project {document.ProjectId.Value}: {document.Title}",
                                NotificationType.ProjectDocument);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Project document notifications could not be sent for document {DocumentId}", document.DocumentId);
                    }
                }
                results.Add(new UploadResult(file.FileName, document.DocumentId, "Pending", true, "Uploaded safely pending scan"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Document upload failed safely");
                if (document is not null)
                {
                    _db.Documents.Remove(document);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                try { await _storage.DeleteAsync(key, cancellationToken); } catch { }
                results.Add(new UploadResult(file.FileName, null, "Rejected", false, "Storage or metadata failure"));
            }
        }
        return results;
    }

    private async Task<IQueryable<Document>> AccessibleQueryAsync(int actorUserId)
    {
        var department = await _db.Users.Where(u => u.UserId == actorUserId).Select(u => u.Department).FirstOrDefaultAsync();
        var projectIds = await _db.ProjectMembers.Where(pm => pm.UserId == actorUserId).Select(pm => pm.ProjectId).ToListAsync();
        var shareDocumentIds = await _db.DocumentShares
            .Where(s => s.TargetUserId == actorUserId || (!string.IsNullOrEmpty(department) && s.TargetDepartment == department))
            .Select(s => s.DocumentId).ToListAsync();
        return _db.Documents.AsNoTracking().Include(d => d.Tags).Where(d => d.ScanStatus == ScanStatus.Available &&
            (d.UploaderId == actorUserId || (d.ProjectId.HasValue && projectIds.Contains(d.ProjectId.Value)) || shareDocumentIds.Contains(d.DocumentId)));
    }

    public async Task<Document?> GetAsync(int actorUserId, int documentId)
    {
        var allowed = await _auth.CanViewAsync(actorUserId, documentId);
        if (!allowed) allowed = await _auth.CanInspectScanAsync(actorUserId, documentId);
        if (!allowed) { DocumentLogging.AuthorizationDenied(_logger, actorUserId, documentId, "view"); return null; }
        var document = await _db.Documents.AsNoTracking().Include(d => d.Tags).Include(d => d.Shares)
            .FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (document?.ScanStatus == ScanStatus.Quarantined && !await _auth.CanRetryQuarantineAsync(actorUserId))
            document.QuarantineFailureDetails = null;
        return document;
    }
    public async Task<IReadOnlyList<Document>> MineAsync(int actorUserId, string? sortBy = null, string? category = null,
        int? projectId = null, DateTime? from = null, DateTime? to = null)
    {
        var query = _db.Documents.AsNoTracking().Include(d => d.Tags).Include(d => d.Project)
            .Where(d => d.UploaderId == actorUserId && d.ScanStatus == ScanStatus.Available);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(d => d.Category == category);
        if (projectId.HasValue) query = query.Where(d => d.ProjectId == projectId);
        if (from.HasValue) query = query.Where(d => d.UploadDateUtc >= from.Value);
        if (to.HasValue) query = query.Where(d => d.UploadDateUtc < to.Value.AddDays(1));
        return sortBy?.ToLowerInvariant() switch
        {
            "title" => await query.OrderBy(d => d.Title).Take(500).ToListAsync(),
            "category" => await query.OrderBy(d => d.Category).ThenBy(d => d.Title).Take(500).ToListAsync(),
            "size" => await query.OrderBy(d => d.FileSizeBytes).Take(500).ToListAsync(),
            "date-asc" => await query.OrderBy(d => d.UploadDateUtc).Take(500).ToListAsync(),
            _ => await query.OrderByDescending(d => d.UploadDateUtc).Take(500).ToListAsync()
        };
    }
    public async Task<IReadOnlyList<Document>> SharedAsync(int actorUserId)
    {
        var q = await AccessibleQueryAsync(actorUserId);
        var department = await _db.Users.Where(u => u.UserId == actorUserId).Select(u => u.Department).FirstOrDefaultAsync();
        var ids = await _db.DocumentShares.Where(s => s.TargetUserId == actorUserId ||
            (!string.IsNullOrEmpty(department) && s.TargetDepartment == department)).Select(s => s.DocumentId).ToListAsync();
        return await q.Where(d => ids.Contains(d.DocumentId)).OrderByDescending(d => d.UploadDateUtc).ToListAsync();
    }
    public async Task<IReadOnlyList<Document>> ProjectAsync(int actorUserId, int projectId)
    {
        if (!_auth.HasActiveProjectMembership(actorUserId, projectId)) return Array.Empty<Document>();
        return await _db.Documents.AsNoTracking().Include(d => d.Tags).Where(d => d.ProjectId == projectId && d.ScanStatus == ScanStatus.Available)
            .OrderByDescending(d => d.UploadDateUtc).ToListAsync();
    }
    public async Task<IReadOnlyList<Document>> TaskAsync(int actorUserId, int taskId)
    {
        if (!await _db.Tasks.AsNoTracking().AnyAsync(t => t.TaskId == taskId)) return Array.Empty<Document>();
        var q = await AccessibleQueryAsync(actorUserId);
        return await q.Where(d => d.TaskId == taskId).OrderByDescending(d => d.UploadDateUtc).ToListAsync();
    }
    public async Task<IReadOnlyList<Document>> SearchAsync(int actorUserId, string? query, string? category, int? projectId, DateTime? from, DateTime? to)
    {
        var q = await AccessibleQueryAsync(actorUserId);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(d => d.Title.Contains(term) || (d.Description != null && d.Description.Contains(term)) ||
                d.Uploader.DisplayName.Contains(term) || (d.Project != null && d.Project.Name.Contains(term)) ||
                d.Tags.Any(t => t.Name.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(d => d.Category == category);
        if (projectId.HasValue) q = q.Where(d => d.ProjectId == projectId);
        if (from.HasValue) q = q.Where(d => d.UploadDateUtc >= from.Value);
        if (to.HasValue) q = q.Where(d => d.UploadDateUtc <= to.Value);
        return await q.OrderByDescending(d => d.UploadDateUtc).Take(500).ToListAsync();
    }

    public async Task<bool> UpdateAsync(int actorUserId, int documentId, string title, string? description, string category, int? projectId, IReadOnlyCollection<string>? tags)
    {
        if (!await _auth.CanMaintainAsync(actorUserId, documentId)) return false;
        var d = await _db.Documents.Include(d => d.Tags).FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (d is null || string.IsNullOrWhiteSpace(title) || title.Length > 255 || !DocumentValidation.Categories.Contains(category)) return false;
        if (description?.Length > 2000) return false;
        if (d.TaskId.HasValue)
        {
            var taskProjectId = await _db.Tasks.AsNoTracking().Where(t => t.TaskId == d.TaskId.Value)
                .Select(t => t.ProjectId).FirstOrDefaultAsync();
            if (taskProjectId != projectId) return false;
        }
        if (projectId.HasValue && !await _auth.CanUploadProjectAsync(actorUserId, projectId.Value)) return false;
        d.Title = title.Trim(); d.Description = description?.Trim(); d.Category = category; d.ProjectId = projectId; d.UpdatedDateUtc = DateTime.UtcNow;
        _db.DocumentTags.RemoveRange(d.Tags);
        foreach (var tag in (tags ?? Array.Empty<string>())
            .Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0 && t.Length <= 100).Distinct())
            d.Tags.Add(new DocumentTag { DocumentId = d.DocumentId, Name = tag });
        await AddActivityAsync(d, actorUserId, DocumentActivityType.MetadataChange, DocumentActivityOutcome.Success);
        await _db.SaveChangesAsync(); return true;
    }

    public async Task<bool> ReplaceAsync(int actorUserId, int documentId, UploadFile file)
    {
        if (!await _auth.CanMaintainAsync(actorUserId, documentId)) return false;
        var fileSize = file.DeclaredSizeBytes ?? file.Content.LongLength;
        if (fileSize != file.Content.LongLength ||
            !DocumentValidation.TryValidate(file.FileName, file.ContentType, fileSize, out var supported, out var error)) return false;
        var d = await _db.Documents.FirstOrDefaultAsync(x => x.DocumentId == documentId);
        if (d is null) return false;
        var oldKey = d.StorageKey;
        var newKey = DocumentValidation.GuidStorageKey(file.FileName);
        try
        {
            await using var stream = new MemoryStream(file.Content);
            await _storage.UploadAsync(newKey, stream);
            d.StorageKey = newKey; d.OriginalFileName = Path.GetFileName(file.FileName);
            d.FileSizeBytes = fileSize; d.OriginalFileType = file.ContentType ?? "application/octet-stream";
            d.SupportedFileType = supported!; d.UpdatedDateUtc = DateTime.UtcNow;
            d.ScanStatus = ScanStatus.Pending;
            await _db.SaveChangesAsync();
        }
        catch
        {
            d.StorageKey = oldKey;
            await _storage.DeleteAsync(newKey);
            return false;
        }
        await AddActivityAsync(d, actorUserId, DocumentActivityType.Replace, DocumentActivityOutcome.Success);
        await _db.SaveChangesAsync();
        try { await _storage.DeleteAsync(oldKey); } catch (Exception ex) { _logger.LogWarning(ex, "Old document storage cleanup failed"); }
        var correlation = Guid.NewGuid();
        _db.DocumentScanAttempts.Add(new DocumentScanAttempt { DocumentId = d.DocumentId, CorrelationId = correlation, StorageKey = newKey, AttemptNumber = 1 });
        await _db.SaveChangesAsync();
        try
        {
            await _queue.EnqueueAsync(new DocumentQueueMessage(d.DocumentId, newKey, 1, correlation));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Scan queue unavailable for replacement document {DocumentId}; leaving it Pending", d.DocumentId);
        }
        return true;
    }

    public async Task<bool> DeleteAsync(int actorUserId, int documentId)
    {
        if (!await _auth.CanMaintainAsync(actorUserId, documentId)) return false;
        var d = await _db.Documents.FirstOrDefaultAsync(x => x.DocumentId == documentId);
        if (d is null) return false;
        return await RemoveDocumentAndStorageAsync(d, actorUserId, DocumentActivityType.Delete);
    }

    public async Task<bool> AddShareAsync(int actorUserId, int documentId, int? targetUserId, string? department)
    {
        if (!await _auth.CanMaintainAsync(actorUserId, documentId) ||
            targetUserId.HasValue == !string.IsNullOrWhiteSpace(department)) return false;
        var d = await _db.Documents.FirstOrDefaultAsync(x => x.DocumentId == documentId);
        if (d is null) return false;
        if (targetUserId.HasValue && !await _db.Users.AnyAsync(u => u.UserId == targetUserId.Value)) return false;
        var share = new DocumentShare { DocumentId = documentId, TargetUserId = targetUserId,
            TargetDepartment = string.IsNullOrWhiteSpace(department) ? null : department.Trim(), GrantedByUserId = actorUserId };
        _db.DocumentShares.Add(share);
        await AddActivityAsync(d, actorUserId, DocumentActivityType.Share, DocumentActivityOutcome.Success,
            targetUserId: targetUserId, department: share.TargetDepartment);
        await _db.SaveChangesAsync();
        var recipientIds = targetUserId.HasValue
            ? new[] { targetUserId.Value }
            : await _db.Users.AsNoTracking().Where(u => u.Department == share.TargetDepartment).Select(u => u.UserId).ToArrayAsync();
        try
        {
            foreach (var recipientId in recipientIds.Where(id => id != actorUserId))
                await _notifications.CreateNotificationAsync(recipientId,
                    "Document shared", $"A document was shared with you: {d.Title}", NotificationType.DocumentShare);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Document share notifications could not be sent for document {DocumentId}", documentId);
        }
        return true;
    }
    public async Task<bool> RemoveShareAsync(int actorUserId, int documentId, int shareId)
    {
        if (!await _auth.CanMaintainAsync(actorUserId, documentId)) return false;
        var share = await _db.DocumentShares.FirstOrDefaultAsync(s => s.DocumentShareId == shareId && s.DocumentId == documentId);
        if (share is null) return false;
        _db.DocumentShares.Remove(share); await _db.SaveChangesAsync(); return true;
    }
    public async Task<(int Count, IReadOnlyList<Document> Latest)> DashboardAsync(int actorUserId)
    {
        var count = await _db.Documents.CountAsync(d => d.UploaderId == actorUserId && d.ScanStatus == ScanStatus.Available);
        var latest = await _db.Documents.AsNoTracking().Where(d => d.UploaderId == actorUserId && d.ScanStatus == ScanStatus.Available)
            .OrderByDescending(d => d.UploadDateUtc).Take(5).ToListAsync();
        return (count, latest);
    }
    public async Task<bool> RetryQuarantineAsync(int actorUserId, int documentId)
    {
        if (!await _auth.CanRetryQuarantineAsync(actorUserId)) return false;
        var d = await _db.Documents.FirstOrDefaultAsync(x => x.DocumentId == documentId && x.ScanStatus == ScanStatus.Quarantined);
        if (d is null) return false;
        d.ScanStatus = ScanStatus.Pending;
        d.QuarantineFailureDetails = null;
        d.QuarantinedUntilUtc = null;
        var correlation = Guid.NewGuid();
        _db.DocumentScanAttempts.Add(new DocumentScanAttempt { DocumentId = d.DocumentId, CorrelationId = correlation,
            StorageKey = d.StorageKey, AttemptNumber = await _db.DocumentScanAttempts.CountAsync(a => a.DocumentId == d.DocumentId) + 1,
            Status = DocumentScanAttemptStatus.Pending });
        await AddActivityAsync(d, actorUserId, DocumentActivityType.QuarantineRetry, DocumentActivityOutcome.Success);
        await _db.SaveChangesAsync();
        try
        {
            await _queue.EnqueueAsync(new DocumentQueueMessage(d.DocumentId, d.StorageKey,
                await _db.DocumentScanAttempts.CountAsync(a => a.DocumentId == d.DocumentId), correlation));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Quarantine retry queue unavailable for document {DocumentId}; leaving it Pending", documentId);
        }
        return true;
    }
    public async Task<bool> PurgeQuarantineAsync(int actorUserId, int documentId)
    {
        if (!await _auth.CanRetryQuarantineAsync(actorUserId)) return false;
        var d = await _db.Documents.FirstOrDefaultAsync(x => x.DocumentId == documentId && x.ScanStatus == ScanStatus.Quarantined);
        if (d is null) return false;
        return await RemoveDocumentAndStorageAsync(d, actorUserId, DocumentActivityType.Purge);
    }

    private async Task<bool> RemoveDocumentAndStorageAsync(Document document, int actorUserId, DocumentActivityType activityType)
    {
        byte[]? backup = null;
        try
        {
            await using var oldFile = await _storage.OpenReadAsync(document.StorageKey);
            if (oldFile is not null)
            {
                using var buffer = new MemoryStream();
                await oldFile.CopyToAsync(buffer);
                backup = buffer.ToArray();
            }
            await _storage.DeleteAsync(document.StorageKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document storage deletion failed for document {DocumentId}", document.DocumentId);
            return false;
        }

        await AddActivityAsync(document, actorUserId, activityType, DocumentActivityOutcome.Success);
        _db.Documents.Remove(document);
        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document metadata deletion failed for document {DocumentId}; attempting storage compensation", document.DocumentId);
            if (backup is not null)
            {
                try
                {
                    await using var content = new MemoryStream(backup, writable: false);
                    await _storage.UploadAsync(document.StorageKey, content);
                }
                catch (Exception compensationError)
                {
                    _logger.LogCritical(compensationError, "Could not restore file for document {DocumentId} after metadata deletion failure", document.DocumentId);
                }
            }
            return false;
        }
    }
    public async Task RecordDownloadAsync(int actorUserId, int documentId)
    {
        if (!await _auth.CanViewAsync(actorUserId, documentId))
        {
            DocumentLogging.AuthorizationDenied(_logger, actorUserId, documentId, "download");
            return;
        }
        var d = await _db.Documents.FirstOrDefaultAsync(x => x.DocumentId == documentId);
        if (d is null || d.ScanStatus != ScanStatus.Available) return;
        await AddActivityAsync(d, actorUserId, DocumentActivityType.Download, DocumentActivityOutcome.Success);
        await _db.SaveChangesAsync();
    }
}
