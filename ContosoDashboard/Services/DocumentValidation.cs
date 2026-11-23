namespace ContosoDashboard.Services;

public static class DocumentValidation
{
    public const long MaxFileSizeBytes = 26214400;
    public static readonly IReadOnlySet<string> Categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "General", "Legal", "Finance", "HR", "Engineering", "Project", "Training" };
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".txt"] = "text/plain",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png"
    };
    public static bool TryValidate(string fileName, string? contentType, long size, out string? supportedType, out string? error)
    {
        supportedType = null; error = null;
        if (size < 1 || size > MaxFileSizeBytes) { error = "File must be between 1 byte and 25 MB"; return false; }
        var unsafeName = fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || fileName.Contains("..");
        if (unsafeName) { error = "Unsafe file name"; return false; }
        var extension = Path.GetExtension(fileName);
        if (!Allowed.TryGetValue(extension, out var expectedMime) ||
            !string.Equals(expectedMime, contentType?.Split(';')[0].Trim(), StringComparison.OrdinalIgnoreCase))
        { error = "Unsupported file type or MIME type"; return false; }
        supportedType = extension.TrimStart('.').ToUpperInvariant().Replace("JPEG", "JPEG").Replace("JPG", "JPEG");
        return true;
    }
    public static string GuidStorageKey(string fileName) => $"{Guid.NewGuid():N}{Path.GetExtension(fileName).ToLowerInvariant()}";

    public static bool IsValidStorageKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key != Path.GetFileName(key) || key.Contains('/') || key.Contains('\\'))
            return false;
        var stem = Path.GetFileNameWithoutExtension(key);
        return Guid.TryParseExact(stem, "N", out _) &&
               Allowed.ContainsKey(Path.GetExtension(key));
    }
}
