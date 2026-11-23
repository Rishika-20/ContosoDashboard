namespace ContosoDashboard.Services;

public sealed record DocumentQueueMessage(int DocumentId, string StorageKey, int Attempt, Guid CorrelationId);
public sealed class DocumentQueueRetryOptions
{
    public int MaxDequeueCount { get; set; } = 5;
    public string PoisonQueueName { get; set; } = "document-scans-poison";
}
