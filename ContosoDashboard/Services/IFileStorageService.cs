namespace ContosoDashboard.Services;

public interface IFileStorageService
{
    Task<string> UploadAsync(string relativeKey, Stream content, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string relativeKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(string relativeKey, CancellationToken cancellationToken = default);
}
