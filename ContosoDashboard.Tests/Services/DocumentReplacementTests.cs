using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestFixtures;
using ContosoDashboard.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContosoDashboard.Tests.Services;

public class DocumentReplacementTests
{
    [Fact]
    public async Task Storage_failure_retains_the_original_file_and_metadata()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        var old = await env.AddDocumentAsync();
        var oldKey = old.StorageKey;
        env.Storage.FailUpload = true;

        var replaced = await env.Service.ReplaceAsync(101, 1,
            new UploadFile(new byte[] { 9, 8, 7 }, "new.pdf", "application/pdf"));

        Assert.False(replaced);
        var saved = await env.Db.Documents.FindAsync(1);
        Assert.Equal(oldKey, saved!.StorageKey);
        Assert.Equal(ScanStatus.Available, saved.ScanStatus);
        Assert.Contains(oldKey, env.Storage.Files.Keys);
    }

    [Fact]
    public async Task Successful_replacement_preserves_identity_and_queues_a_new_scan()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        var old = await env.AddDocumentAsync();
        var oldKey = old.StorageKey;

        Assert.True(await env.Service.ReplaceAsync(101, 1,
            new UploadFile(new byte[] { 9, 8, 7 }, "new.pdf", "application/pdf")));

        var saved = await env.Db.Documents.FindAsync(1);
        Assert.Equal(1, saved!.DocumentId);
        Assert.NotEqual(oldKey, saved.StorageKey);
        Assert.False(env.Storage.Files.ContainsKey(oldKey));
        Assert.Equal(ScanStatus.Pending, saved.ScanStatus);
        Assert.Single(env.Queue.Messages);
    }

    [Fact]
    public async Task Metadata_failure_rolls_back_replacement_and_keeps_old_file()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new FailingSaveDbContext(options);
        db.Users.Add(new User { UserId = 101, Email = "owner@example.test", DisplayName = "Owner" });
        db.Documents.Add(new Document
        {
            DocumentId = 1, UploaderId = 101, Title = "Original", Category = "General",
            OriginalFileName = "original.pdf", FileSizeBytes = 3, OriginalFileType = "application/pdf",
            SupportedFileType = "PDF", StorageKey = "f9be1ec3634f4cf19c987ca8d3648c78.pdf",
            ScanStatus = ScanStatus.Available
        });
        await db.SaveChangesAsync();
        var storage = new FakeFileStorage();
        storage.Files["f9be1ec3634f4cf19c987ca8d3648c78.pdf"] = new byte[] { 1, 2, 3 };
        var service = new DocumentService(db, storage, new FakeDocumentQueue(), new DocumentAuthorizationService(db),
            NullLogger<DocumentService>.Instance, new FakeNotifications());
        db.FailNextSave = true;

        var replaced = await service.ReplaceAsync(101, 1,
            new UploadFile(new byte[] { 9, 8, 7 }, "new.pdf", "application/pdf"));

        Assert.False(replaced);
        db.ChangeTracker.Clear();
        var persisted = await db.Documents.SingleAsync();
        Assert.Equal("Original", persisted.Title);
        Assert.Equal("f9be1ec3634f4cf19c987ca8d3648c78.pdf", persisted.StorageKey);
        Assert.Single(storage.Files);
    }

    private sealed class FailingSaveDbContext(DbContextOptions<ApplicationDbContext> options)
        : ApplicationDbContext(options)
    {
        public bool FailNextSave { get; set; }
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new InvalidOperationException("Simulated metadata failure");
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
