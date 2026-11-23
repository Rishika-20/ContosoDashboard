using ContosoDashboard.Services;

namespace ContosoDashboard.Tests.Services;

public class DocumentStorageAndValidationTests
{
    [Fact]
    public void RejectsOversizeAndUnsupportedFiles()
    {
        Assert.False(DocumentValidation.TryValidate("virus.exe", "application/octet-stream", 100, out _, out _));
        Assert.False(DocumentValidation.TryValidate("large.pdf", "application/pdf", 26214401, out _, out _));
    }
    [Fact]
    public void AcceptsPdfAndCreatesGuidRelativeKey()
    {
        Assert.True(DocumentValidation.TryValidate("plan.pdf", "application/pdf", 100, out var type, out var error));
        Assert.Equal("PDF", type);
        Assert.Null(error);
        var key = DocumentValidation.GuidStorageKey("plan.pdf");
        Assert.DoesNotContain("/", key);
        Assert.EndsWith(".pdf", key);
        Assert.True(DocumentValidation.IsValidStorageKey(key));
    }

    [Theory]
    [InlineData("../secret.pdf")]
    [InlineData("folder/file.pdf")]
    [InlineData("not-a-guid.pdf")]
    [InlineData("3e5a7495360344dca6a359bca931b267.exe")]
    public void Rejects_unsafe_storage_keys(string key)
    {
        Assert.False(DocumentValidation.IsValidStorageKey(key));
    }

    [Fact]
    public async Task Test_scanner_quarantines_the_EICAR_test_signature()
    {
        var scanner = new TestableMalwareScanner();
        await using var content = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(
            @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"));

        var result = await scanner.ScanAsync("test.pdf", content);

        Assert.Equal(ScanResultStatus.Quarantined, result.Status);
    }
}
