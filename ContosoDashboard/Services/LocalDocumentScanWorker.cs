using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Services;

/// <summary>
/// Polls pending local scan attempts when Azure Queue Storage is not being used.
/// The worker never changes a document to Available; only DocumentScanProcessor can do that
/// after the configured malware scanner succeeds.
/// </summary>
public sealed class LocalDocumentScanWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LocalDocumentScanWorker> _logger;

    public LocalDocumentScanWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration,
        ILogger<LocalDocumentScanWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue<bool>("DocumentUpload:UseLocalScanWorker"))
            return;

        var maxAttempts = Math.Clamp(_configuration.GetValue<int?>("DocumentUpload:LocalScanMaxAttempts") ?? 5, 1, 10);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var processor = scope.ServiceProvider.GetRequiredService<DocumentScanProcessor>();
                var pending = await db.DocumentScanAttempts
                    .AsNoTracking()
                    .Where(a => (a.Status == DocumentScanAttemptStatus.Pending ||
                                 a.Status == DocumentScanAttemptStatus.TransientFailure) &&
                                a.AttemptNumber < maxAttempts &&
                                a.Document.ScanStatus == ScanStatus.Pending)
                    .OrderBy(a => a.EnqueuedAtUtc)
                    .Select(a => new DocumentQueueMessage(a.DocumentId, a.StorageKey, a.AttemptNumber + 1, a.CorrelationId))
                    .FirstOrDefaultAsync(stoppingToken);

                if (pending is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                await processor.ProcessAsync(pending, stoppingToken);
                var attempt = await db.DocumentScanAttempts.FirstOrDefaultAsync(
                    a => a.DocumentId == pending.DocumentId && a.CorrelationId == pending.CorrelationId, stoppingToken);
                if (attempt is not null)
                {
                    attempt.AttemptNumber = pending.Attempt;
                    if (attempt.Status == DocumentScanAttemptStatus.TransientFailure && attempt.AttemptNumber >= maxAttempts)
                        attempt.Status = DocumentScanAttemptStatus.Poisoned;
                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Local document scan worker iteration failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
