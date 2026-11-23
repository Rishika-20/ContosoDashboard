using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestFixtures;
using ContosoDashboard.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContosoDashboard.Tests.Services;

public class DocumentUploadTests
{
    [Fact]
    public async Task Batch_upload_processes_each_file_independently()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        var files = new[]
        {
            new UploadFile(new byte[] { 1, 2, 3 }, "plan.pdf", "application/pdf"),
            new UploadFile(new byte[] { 4 }, "blocked.exe", "application/octet-stream")
        };

        var result = await env.Service.UploadAsync(101,
            new UploadRequest("  Project plan  ", null, "General", null, null, null), files);

        Assert.Equal(2, result.Count);
        Assert.True(result[0].Success);
        Assert.Equal("Pending", result[0].Status);
        Assert.False(result[1].Success);
        Assert.Contains("Unsupported", result[1].Message);
        Assert.Single(env.Db.Documents);
        Assert.Single(env.Queue.Messages);
    }

    [Fact]
    public async Task Queue_outage_keeps_file_private_in_pending_state_for_later_processing()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        env.Queue.FailEnqueue = true;

        var result = await env.Service.UploadAsync(101,
            new UploadRequest("Pending document", null, "General", null, null, null),
            new[] { new UploadFile(new byte[] { 1, 2, 3 }, "pending.pdf", "application/pdf") });

        Assert.True(result.Single().Success);
        var document = Assert.Single(env.Db.Documents);
        Assert.Equal(ScanStatus.Pending, document.ScanStatus);
        Assert.False(await new DocumentAuthorizationService(env.Db).CanViewAsync(101, document.DocumentId));
        Assert.Contains(document.StorageKey, env.Storage.Files.Keys);
    }

    [Fact]
    public async Task Storage_failure_does_not_create_document_metadata()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        env.Storage.FailUpload = true;

        var result = await env.Service.UploadAsync(101,
            new UploadRequest("Storage failure", null, "General", null, null, null),
            new[] { new UploadFile(new byte[] { 1 }, "failed.pdf", "application/pdf") });

        Assert.False(result.Single().Success);
        Assert.Empty(env.Db.Documents);
        Assert.Empty(env.Queue.Messages);
    }

    [Fact]
    public async Task Mismatched_declared_size_is_rejected_without_persisting_bytes()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();

        var result = await env.Service.UploadAsync(101,
            new UploadRequest("Incorrect size", null, "General", null, null, null),
            new[] { new UploadFile(new byte[] { 1 }, "incorrect.pdf", "application/pdf", DeclaredSizeBytes: 2) });

        Assert.False(result.Single().Success);
        Assert.Contains("size did not match", result.Single().Message);
        Assert.Empty(env.Db.Documents);
        Assert.Empty(env.Storage.Files);
    }

    [Fact]
    public void Retry_selection_contains_only_failed_files()
    {
        var files = new[] { "success.pdf", "retry.pdf" };
        var results = new[]
        {
            new UploadResult("success.pdf", 1, "Pending", true, "Uploaded"),
            new UploadResult("retry.pdf", null, "Rejected", false, "Retry")
        };

        Assert.Equal("retry.pdf", Assert.Single(UploadRetryPolicy.FailedFiles(files, results)));
    }

    [Fact]
    public async Task Metadata_failure_removes_persisted_metadata_and_uploaded_bytes()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new FailingSaveDbContext(options);
        db.Users.Add(new User { UserId = 101, Email = "owner@example.test", DisplayName = "Owner" });
        await db.SaveChangesAsync();
        db.FailOnSaveNumber = db.SaveCount + 2;
        var storage = new FakeFileStorage();
        var queue = new FakeDocumentQueue();
        var service = new DocumentService(db, storage, queue, new DocumentAuthorizationService(db),
            NullLogger<DocumentService>.Instance, new FakeNotifications());

        var result = await service.UploadAsync(101,
            new UploadRequest("Metadata failure", null, "General", null, null, null),
            new[] { new UploadFile(new byte[] { 1, 2 }, "fail.pdf", "application/pdf") });

        Assert.False(result.Single().Success);
        Assert.Empty(db.Documents);
        Assert.Empty(storage.Files);
        Assert.Empty(queue.Messages);
    }

    private sealed class FailingSaveDbContext(DbContextOptions<ApplicationDbContext> options)
        : ApplicationDbContext(options)
    {
        public int SaveCount { get; private set; }
        public int FailOnSaveNumber { get; set; }
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (SaveCount == FailOnSaveNumber)
                throw new InvalidOperationException("Simulated metadata failure");
            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
