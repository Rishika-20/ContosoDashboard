using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocumentScanner.Function.Tests;

public class DocumentScanFunctionTests
{
    [Theory]
    [InlineData(ScanResultStatus.Available, ScanStatus.Available, DocumentScanAttemptStatus.Available)]
    [InlineData(ScanResultStatus.Quarantined, ScanStatus.Quarantined, DocumentScanAttemptStatus.Quarantined)]
    [InlineData(ScanResultStatus.TransientFailure, ScanStatus.Pending, DocumentScanAttemptStatus.TransientFailure)]
    public async Task Processor_records_scan_result_and_keeps_non_success_private(
        ScanResultStatus scannerStatus, ScanStatus expectedDocumentStatus, DocumentScanAttemptStatus expectedAttemptStatus)
    {
        await using var db = CreateContext();
        await SeedDocumentAsync(db);
        var storage = new FunctionTestStorage();
        storage.Files["8ef41ad75188436b9a2c6e8dfb412345.pdf"] = new byte[] { 1 };
        var scanner = new FunctionTestScanner(scannerStatus);
        var processor = new DocumentScanProcessor(db, storage, scanner);
        var correlation = Guid.NewGuid();

        var result = await processor.ProcessAsync(new DocumentQueueMessage(42,
            "8ef41ad75188436b9a2c6e8dfb412345.pdf", 1, correlation));

        var document = await db.Documents.SingleAsync();
        var attempt = await db.DocumentScanAttempts.SingleAsync();
        Assert.Equal(scannerStatus, result);
        Assert.Equal(expectedDocumentStatus, document.ScanStatus);
        Assert.Equal(expectedAttemptStatus, attempt.Status);
        if (scannerStatus == ScanResultStatus.Quarantined)
        {
            Assert.NotNull(document.QuarantinedUntilUtc);
            Assert.NotNull(document.QuarantineFailureDetails);
            var daysRemaining = (document.QuarantinedUntilUtc!.Value - DateTime.UtcNow).TotalDays;
            Assert.InRange(daysRemaining, 29.99, 30.01);
        }
        if (scannerStatus == ScanResultStatus.TransientFailure)
            Assert.Null(document.QuarantineFailureDetails);
    }

    [Fact]
    public async Task Successful_correlation_is_idempotent()
    {
        await using var db = CreateContext();
        await SeedDocumentAsync(db);
        var storage = new FunctionTestStorage();
        storage.Files["8ef41ad75188436b9a2c6e8dfb412345.pdf"] = new byte[] { 1 };
        var scanner = new FunctionTestScanner(ScanResultStatus.Available);
        var processor = new DocumentScanProcessor(db, storage, scanner);
        var message = new DocumentQueueMessage(42, "8ef41ad75188436b9a2c6e8dfb412345.pdf", 1, Guid.NewGuid());

        Assert.Equal(ScanResultStatus.Available, await processor.ProcessAsync(message));
        Assert.Equal(ScanResultStatus.Available, await processor.ProcessAsync(message));
        Assert.Equal(1, scanner.ScanCount);
    }

    [Fact]
    public async Task Poisoned_message_marks_attempt_without_exposing_document()
    {
        await using var db = CreateContext();
        await SeedDocumentAsync(db);
        var storage = new FunctionTestStorage();
        storage.Files["8ef41ad75188436b9a2c6e8dfb412345.pdf"] = new byte[] { 1 };
        var processor = new DocumentScanProcessor(db, storage, new FunctionTestScanner(ScanResultStatus.TransientFailure));
        var message = new DocumentQueueMessage(42, "8ef41ad75188436b9a2c6e8dfb412345.pdf", 5, Guid.NewGuid());

        await processor.ProcessAsync(message);
        await processor.MarkPoisonedAsync(message);

        Assert.Equal(DocumentScanAttemptStatus.Poisoned, (await db.DocumentScanAttempts.SingleAsync()).Status);
        Assert.Equal(ScanStatus.Pending, (await db.Documents.SingleAsync()).ScanStatus);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        return new ApplicationDbContext(options);
    }

    private static async Task SeedDocumentAsync(ApplicationDbContext db)
    {
        db.Users.Add(new User { UserId = 41, Email = "uploader@example.test", DisplayName = "Uploader" });
        db.Documents.Add(new Document
        {
            DocumentId = 42, UploaderId = 41, Title = "Scan test", Category = "General",
            OriginalFileName = "test.pdf", FileSizeBytes = 1, OriginalFileType = "application/pdf",
            SupportedFileType = "PDF", StorageKey = "8ef41ad75188436b9a2c6e8dfb412345.pdf",
            ScanStatus = ScanStatus.Pending
        });
        await db.SaveChangesAsync();
    }

    private sealed class FunctionTestStorage : IFileStorageService
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        public Task<string> UploadAsync(string relativeKey, Stream content, CancellationToken cancellationToken = default)
            => Task.FromResult(relativeKey);
        public Task<Stream?> OpenReadAsync(string relativeKey, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(Files.TryGetValue(relativeKey, out var bytes) ? new MemoryStream(bytes) : null);
        public Task DeleteAsync(string relativeKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FunctionTestScanner(ScanResultStatus status) : IMalwareScanner
    {
        public int ScanCount { get; private set; }
        public Task<ScanResult> ScanAsync(string storageKey, Stream content, CancellationToken cancellationToken = default)
        {
            ScanCount++;
            return Task.FromResult(new ScanResult(status, status == ScanResultStatus.Quarantined ? "Test signature detected" : null));
        }
    }
}
