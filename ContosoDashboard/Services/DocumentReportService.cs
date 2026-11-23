using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Services;

public sealed record FileTypeReport(string FileType, int Count);
public sealed record UploaderReport(int UserId, int Count);
public sealed record DocumentAccessReport(int ActorUserId, DocumentActivityOutcome Outcome, int Count);
public sealed record DocumentActivityReport(
    IReadOnlyList<FileTypeReport> FileTypes,
    IReadOnlyList<UploaderReport> ActiveUploaders,
    IReadOnlyList<DocumentAccessReport> AccessPatterns);

public sealed class DocumentReportService
{
    private readonly ApplicationDbContext _db;
    public DocumentReportService(ApplicationDbContext db) => _db = db;
    public async Task<DocumentActivityReport> GetActivityReportAsync(int actorUserId)
    {
        var isAdmin = await _db.Users.Where(u => u.UserId == actorUserId).Select(u => u.Role).FirstOrDefaultAsync() == UserRole.Administrator;
        if (!isAdmin) throw new UnauthorizedAccessException();
        var fileTypes = await _db.Documents.AsNoTracking().GroupBy(d => d.SupportedFileType)
            .Select(g => new FileTypeReport(g.Key, g.Count())).ToListAsync();
        var activeUploaders = await _db.Documents.AsNoTracking().GroupBy(d => d.UploaderId)
            .Select(g => new UploaderReport(g.Key, g.Count())).ToListAsync();
        var access = await _db.DocumentActivities.AsNoTracking().Where(a => a.ActivityType == DocumentActivityType.Download)
            .GroupBy(a => new { a.ActorUserId, a.Outcome })
            .Select(g => new DocumentAccessReport(g.Key.ActorUserId, g.Key.Outcome, g.Count())).ToListAsync();
        return new DocumentActivityReport(fileTypes, activeUploaders, access);
    }
}
