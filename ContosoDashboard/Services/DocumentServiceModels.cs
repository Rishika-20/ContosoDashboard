namespace ContosoDashboard.Services;

public sealed record UploadFile(byte[] Content, string FileName, string? ContentType, long? DeclaredSizeBytes = null);
public sealed record UploadRequest(string Title, string? Description, string Category, int? ProjectId, int? TaskId, IReadOnlyCollection<string>? Tags);
public sealed record UploadResult(string OriginalFileName, int? DocumentId, string Status, bool Success, string Message);
public sealed record DocumentListItem(int DocumentId, string Title, string Category, long FileSizeBytes, DateTime UploadDateUtc, string ScanStatus);

public static class UploadRetryPolicy
{
    public static IReadOnlyList<T> FailedFiles<T>(IReadOnlyList<T> files, IReadOnlyList<UploadResult> results)
    {
        var retry = new List<T>();
        for (var index = 0; index < files.Count; index++)
        {
            if (index >= results.Count || !results[index].Success)
                retry.Add(files[index]);
        }
        return retry;
    }
}
