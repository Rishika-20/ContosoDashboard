using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestFixtures;

namespace ContosoDashboard.Tests.Services;

public class DocumentScanQueueTests
{
    [Fact]
    public async Task Queue_message_carries_idempotency_correlation_and_attempt()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.Service.UploadAsync(101,
            new UploadRequest("Queue test", null, "General", null, null, null),
            new[] { new UploadFile(new byte[] { 1 }, "queue.pdf", "application/pdf") });

        var queued = Assert.Single(env.Queue.Messages);
        var attempt = Assert.Single(env.Db.DocumentScanAttempts);
        Assert.Equal(attempt.DocumentId, queued.DocumentId);
        Assert.Equal(attempt.StorageKey, queued.StorageKey);
        Assert.Equal(attempt.CorrelationId, queued.CorrelationId);
        Assert.Equal(1, queued.Attempt);
    }

    [Fact]
    public void Retry_configuration_is_bounded_and_uses_poison_queue()
    {
        var options = new DocumentQueueRetryOptions();
        Assert.InRange(options.MaxDequeueCount, 1, 10);
        Assert.Equal("document-scans-poison", options.PoisonQueueName);
    }
}
