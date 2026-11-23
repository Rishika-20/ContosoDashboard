namespace ContosoDashboard.Services;

public sealed class LocalFileStorageService : IFileStorageService
{
    private readonly string _root;
    private readonly ILogger<LocalFileStorageService> _logger;
    public LocalFileStorageService(IConfiguration configuration, IWebHostEnvironment environment, ILogger<LocalFileStorageService> logger)
    {
        var configured = configuration["DocumentUpload:LocalRoot"] ?? "AppData/uploads";
        _root = Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured);
        _logger = logger;
        Directory.CreateDirectory(_root);
    }
    public async Task<string> UploadAsync(string relativeKey, Stream content, CancellationToken cancellationToken = default)
    {
        if (!DocumentValidation.IsValidStorageKey(relativeKey))
            throw new ArgumentException("Invalid storage key", nameof(relativeKey));
        var destination = Path.Combine(_root, relativeKey);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 81920, FileOptions.Asynchronous);
        await content.CopyToAsync(output, cancellationToken);
        _logger.LogInformation("Document storage wrote key {StorageKey}", relativeKey);
        return relativeKey;
    }
    public Task<Stream?> OpenReadAsync(string relativeKey, CancellationToken cancellationToken = default)
    {
        if (!DocumentValidation.IsValidStorageKey(relativeKey))
            throw new ArgumentException("Invalid storage key", nameof(relativeKey));
        var path = Path.Combine(_root, relativeKey);
        Stream? stream = File.Exists(path) ? File.OpenRead(path) : null;
        return Task.FromResult(stream);
    }
    public Task DeleteAsync(string relativeKey, CancellationToken cancellationToken = default)
    {
        if (!DocumentValidation.IsValidStorageKey(relativeKey))
            throw new ArgumentException("Invalid storage key", nameof(relativeKey));
        var path = Path.Combine(_root, relativeKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}
