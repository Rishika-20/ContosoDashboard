using Azure.Storage.Queues;
using Azure.Identity;
using System.Text.Json;

namespace ContosoDashboard.Services;

public sealed class DocumentQueueService : IDocumentQueueService
{
    private readonly ILogger<DocumentQueueService> _logger;
    private readonly IConfiguration _configuration;
    public DocumentQueueService(IConfiguration configuration, ILogger<DocumentQueueService> logger)
    { _configuration = configuration; _logger = logger; }
    public async Task EnqueueAsync(DocumentQueueMessage message, CancellationToken cancellationToken = default)
    {
        if (_configuration.GetValue<bool>("DocumentUpload:UseLocalScanWorker"))
        {
            _logger.LogDebug("Azure queue enqueue skipped because the local scan worker is enabled for document {DocumentId}", message.DocumentId);
            return;
        }

        var connection = _configuration["AzureWebJobsStorage"];
        var queueName = _configuration["DocumentQueue"] ?? "document-scans";
        if (string.IsNullOrWhiteSpace(connection))
        {
            var serviceUri = _configuration["DocumentQueueServiceUri"];
            if (string.IsNullOrWhiteSpace(serviceUri))
            {
                _logger.LogWarning("Queue not configured; document {DocumentId} remains pending", message.DocumentId);
                return;
            }
            var service = new Uri(serviceUri.EndsWith("/", StringComparison.Ordinal) ? serviceUri : serviceUri + "/");
            var identityQueue = new QueueClient(new Uri(service, queueName), new DefaultAzureCredential());
            await EnqueueAsync(identityQueue, message, cancellationToken);
            return;
        }
        await EnqueueAsync(new QueueClient(connection, queueName), message, cancellationToken);
    }

    private static async Task EnqueueAsync(QueueClient queue, DocumentQueueMessage message,
        CancellationToken cancellationToken)
    {
        await queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await queue.SendMessageAsync(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(message)), cancellationToken: cancellationToken);
    }
}
