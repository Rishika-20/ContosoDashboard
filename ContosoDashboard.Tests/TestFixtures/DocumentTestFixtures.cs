using ContosoDashboard.Models;

namespace ContosoDashboard.Tests.TestFixtures;

public static class DocumentTestFixtures
{
    public static User Owner(int id = 1) => new() { UserId = id, Email = $"owner{id}@contoso.com", DisplayName = "Owner", Department = "Engineering" };
    public static Document Document(int id = 1, int ownerId = 1) => new()
    {
        DocumentId = id,
        UploaderId = ownerId,
        Title = "Test document",
        Category = "General",
        OriginalFileName = "test.pdf",
        FileSizeBytes = 100,
        OriginalFileType = "application/pdf",
        SupportedFileType = "PDF",
        StorageKey = Guid.NewGuid().ToString("N"),
        ScanStatus = ScanStatus.Available
    };
}
