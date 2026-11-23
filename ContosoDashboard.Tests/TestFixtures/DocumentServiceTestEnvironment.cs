using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContosoDashboard.Tests.TestFixtures;

public sealed class DocumentServiceTestEnvironment : IAsyncDisposable
{
    private readonly string _databaseName = Guid.NewGuid().ToString("N");
    public ApplicationDbContext Db { get; }
    public FakeFileStorage Storage { get; } = new();
    public FakeDocumentQueue Queue { get; } = new();
    public FakeNotifications Notifications { get; } = new();
    public DocumentService Service { get; }

    public DocumentServiceTestEnvironment()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(_databaseName).Options;
        Db = new ApplicationDbContext(options);
        Service = new DocumentService(Db, Storage, Queue, new DocumentAuthorizationService(Db),
            NullLogger<DocumentService>.Instance, Notifications);
    }

    public async Task SeedAsync()
    {
        Db.Users.AddRange(
            new User { UserId = 101, Email = "owner@example.test", DisplayName = "Owner", Department = "Engineering", Role = UserRole.Employee },
            new User { UserId = 102, Email = "member@example.test", DisplayName = "Member", Department = "Engineering", Role = UserRole.TeamLead },
            new User { UserId = 103, Email = "outsider@example.test", DisplayName = "Outsider", Department = "Finance", Role = UserRole.ProjectManager },
            new User { UserId = 104, Email = "admin@example.test", DisplayName = "Admin", Department = "IT", Role = UserRole.Administrator });
        Db.Projects.Add(new Project { ProjectId = 501, Name = "Project Alpha", ProjectManagerId = 101 });
        Db.ProjectMembers.AddRange(
            new ProjectMember { ProjectMemberId = 1, ProjectId = 501, UserId = 102, Role = "TeamLead" },
            new ProjectMember { ProjectMemberId = 2, ProjectId = 501, UserId = 101, Role = "Contributor" });
        await Db.SaveChangesAsync();
    }

    public async Task<Document> AddDocumentAsync(int id = 1, int uploaderId = 101, int? projectId = null,
        ScanStatus scanStatus = ScanStatus.Available, string title = "Test document", string storageKey = "f9be1ec3634f4cf19c987ca8d3648c78.pdf")
    {
        var document = new Document
        {
            DocumentId = id, UploaderId = uploaderId, ProjectId = projectId, Title = title, Category = "General",
            OriginalFileName = "test.pdf", FileSizeBytes = 100, OriginalFileType = "application/pdf",
            SupportedFileType = "PDF", StorageKey = storageKey, ScanStatus = scanStatus
        };
        Db.Documents.Add(document);
        await Db.SaveChangesAsync();
        Storage.Files[storageKey] = new byte[] { 1, 2, 3 };
        return document;
    }

    public ValueTask DisposeAsync() => Db.DisposeAsync();
}

public sealed class FakeFileStorage : IFileStorageService
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
    public bool FailUpload { get; set; }
    public bool FailDelete { get; set; }
    public async Task<string> UploadAsync(string relativeKey, Stream content, CancellationToken cancellationToken = default)
    {
        if (FailUpload) throw new IOException("Simulated storage error");
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Files[relativeKey] = buffer.ToArray();
        return relativeKey;
    }
    public Task<Stream?> OpenReadAsync(string relativeKey, CancellationToken cancellationToken = default)
        => Task.FromResult<Stream?>(Files.TryGetValue(relativeKey, out var bytes) ? new MemoryStream(bytes, writable: false) : null);
    public Task DeleteAsync(string relativeKey, CancellationToken cancellationToken = default)
    {
        if (FailDelete) throw new IOException("Simulated delete error");
        Files.Remove(relativeKey);
        return Task.CompletedTask;
    }
}

public sealed class FakeDocumentQueue : IDocumentQueueService
{
    public List<DocumentQueueMessage> Messages { get; } = new();
    public bool FailEnqueue { get; set; }
    public Task EnqueueAsync(DocumentQueueMessage message, CancellationToken cancellationToken = default)
    {
        if (FailEnqueue) throw new IOException("Simulated queue outage");
        Messages.Add(message);
        return Task.CompletedTask;
    }
}

public sealed class FakeNotifications : INotificationService
{
    public List<Notification> Created { get; } = new();
    public Task<List<Notification>> GetUserNotificationsAsync(int userId, bool unreadOnly = false) => Task.FromResult(new List<Notification>());
    public Task<Notification> CreateNotificationAsync(Notification notification) { Created.Add(notification); return Task.FromResult(notification); }
    public Task<bool> MarkAsReadAsync(int notificationId, int requestingUserId) => Task.FromResult(false);
    public Task<int> GetUnreadCountAsync(int userId) => Task.FromResult(0);
    public Task<Notification> CreateNotificationAsync(int userId, string title, string message, NotificationType type)
    {
        var notification = new Notification { UserId = userId, Title = title, Message = message, Type = type };
        Created.Add(notification);
        return Task.FromResult(notification);
    }
}
