namespace ContosoDashboard.Services;

public interface IDocumentQueueService
{
    Task EnqueueAsync(DocumentQueueMessage message, CancellationToken cancellationToken = default);
}
