using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public interface IDocumentService
{
    Task<IReadOnlyList<UploadResult>> UploadAsync(int actorUserId, UploadRequest request, IReadOnlyList<UploadFile> files, CancellationToken cancellationToken = default);
    Task<Document?> GetAsync(int actorUserId, int documentId);
    Task<IReadOnlyList<Document>> MineAsync(int actorUserId, string? sortBy = null, string? category = null,
        int? projectId = null, DateTime? from = null, DateTime? to = null);
    Task<IReadOnlyList<Document>> SharedAsync(int actorUserId);
    Task<IReadOnlyList<Document>> ProjectAsync(int actorUserId, int projectId);
    Task<IReadOnlyList<Document>> TaskAsync(int actorUserId, int taskId);
    Task<IReadOnlyList<Document>> SearchAsync(int actorUserId, string? query, string? category, int? projectId, DateTime? from, DateTime? to);
    Task<bool> UpdateAsync(int actorUserId, int documentId, string title, string? description, string category, int? projectId, IReadOnlyCollection<string>? tags);
    Task<bool> ReplaceAsync(int actorUserId, int documentId, UploadFile file);
    Task<bool> DeleteAsync(int actorUserId, int documentId);
    Task<bool> AddShareAsync(int actorUserId, int documentId, int? targetUserId, string? department);
    Task<bool> RemoveShareAsync(int actorUserId, int documentId, int shareId);
    Task<(int Count, IReadOnlyList<Document> Latest)> DashboardAsync(int actorUserId);
    Task<bool> RetryQuarantineAsync(int actorUserId, int documentId);
    Task<bool> PurgeQuarantineAsync(int actorUserId, int documentId);
    Task RecordDownloadAsync(int actorUserId, int documentId);
}
