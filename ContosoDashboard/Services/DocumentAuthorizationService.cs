using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Services;

public sealed class DocumentAuthorizationService
{
    private readonly ApplicationDbContext _db;
    public DocumentAuthorizationService(ApplicationDbContext db) => _db = db;

    public bool HasActiveProjectMembership(int userId, int projectId)
        => _db.ProjectMembers.Any(pm => pm.UserId == userId && pm.ProjectId == projectId);

    public async Task<bool> CanMaintainAsync(int userId, int documentId)
        => await _db.Documents.AsNoTracking().AnyAsync(d => d.DocumentId == documentId &&
            d.UploaderId == userId && d.ScanStatus != ScanStatus.Quarantined);

    public async Task<bool> CanViewAsync(int userId, int documentId)
    {
        var document = await _db.Documents.AsNoTracking()
            .Include(d => d.Shares).Include(d => d.Uploader)
            .FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (document is null || document.ScanStatus != ScanStatus.Available) return false;
        if (document.UploaderId == userId) return true;
        if (document.ProjectId.HasValue && HasActiveProjectMembership(userId, document.ProjectId.Value)) return true;
        if (document.Shares.Any(s => s.TargetUserId == userId)) return true;
        var department = await _db.Users.Where(u => u.UserId == userId).Select(u => u.Department).FirstOrDefaultAsync();
        return !string.IsNullOrWhiteSpace(department) && document.Shares.Any(s => s.TargetDepartment == department);
    }

    public async Task<bool> CanRetryQuarantineAsync(int userId)
    {
        var role = await _db.Users.Where(u => u.UserId == userId).Select(u => u.Role).FirstOrDefaultAsync();
        return role == UserRole.Administrator;
    }

    public async Task<bool> CanInspectScanAsync(int userId, int documentId)
    {
        var status = await _db.Documents.AsNoTracking()
            .Where(d => d.DocumentId == documentId)
            .Select(d => new { d.UploaderId, d.ScanStatus })
            .FirstOrDefaultAsync();
        if (status is null || status.ScanStatus == ScanStatus.Available) return false;
        if (status.UploaderId == userId) return true;
        return await CanRetryQuarantineAsync(userId);
    }

    public async Task<bool> CanUploadProjectAsync(int userId, int projectId)
    {
        if (!await _db.Projects.AnyAsync(p => p.ProjectId == projectId)) return false;
        return HasActiveProjectMembership(userId, projectId);
    }
}
