using System.Text;
using System.Text.Json;
using ContosoDashboard.Services;
using Microsoft.Azure.Functions.Worker;

namespace DocumentScanner.Function.Functions;

public sealed class ProcessDocumentScan
{
    private readonly DocumentScanProcessor _processor;
    public ProcessDocumentScan(DocumentScanProcessor processor) => _processor = processor;

    [Function("ProcessDocumentScan")]
    public async Task Run([QueueTrigger("document-scans", Connection = "AzureWebJobsStorage")] QueuePayload payload)
    {
        var message = new DocumentQueueMessage(payload.DocumentId, payload.StorageKey, payload.Attempt, payload.CorrelationId);
        var result = await _processor.ProcessAsync(message);
        if (result == ScanResultStatus.TransientFailure)
            throw new InvalidOperationException("Transient document scan infrastructure failure");
    }

    [Function("HandlePoisonedDocumentScan")]
    public async Task HandlePoisoned([QueueTrigger("document-scans-poison", Connection = "AzureWebJobsStorage")] QueuePayload payload)
    {
        await _processor.MarkPoisonedAsync(new DocumentQueueMessage(
            payload.DocumentId, payload.StorageKey, payload.Attempt, payload.CorrelationId));
    }
}

public sealed class QueuePayload
{
    public int DocumentId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public int Attempt { get; set; }
    public Guid CorrelationId { get; set; }
}
