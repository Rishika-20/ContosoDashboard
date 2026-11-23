using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestFixtures;

namespace ContosoDashboard.Tests.Services;

public class DocumentDashboardReportTests
{
    [Fact]
    public async Task Dashboard_returns_count_and_five_latest_available_documents_for_owner()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        for (var index = 1; index <= 7; index++)
        {
            var document = await env.AddDocumentAsync(index, title: $"Document {index}",
                storageKey: $"f9be1ec3634f4cf19c987ca8d3648c{index:x2}.pdf");
            document.UploadDateUtc = DateTime.UtcNow.AddMinutes(index);
        }
        await env.Db.SaveChangesAsync();
        await env.AddDocumentAsync(id: 8, uploaderId: 102, title: "Other user's file",
            storageKey: "f9be1ec3634f4cf19c987ca8d3648c88.pdf");

        var result = await env.Service.DashboardAsync(101);

        Assert.Equal(7, result.Count);
        Assert.Equal(5, result.Latest.Count);
        Assert.Equal("Document 7", result.Latest[0].Title);
    }

    [Fact]
    public async Task Reports_are_administrator_only_and_aggregate_activity_without_file_content()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.AddDocumentAsync();
        env.Db.DocumentActivities.Add(new DocumentActivity
        {
            DocumentId = 1, ActorUserId = 102, ActivityType = DocumentActivityType.Download,
            Outcome = DocumentActivityOutcome.Success
        });
        await env.Db.SaveChangesAsync();
        var reportService = new DocumentReportService(env.Db);

        var report = await reportService.GetActivityReportAsync(104);

        Assert.Contains(report.FileTypes, type => type.FileType == "PDF" && type.Count == 1);
        Assert.Contains(report.ActiveUploaders, uploader => uploader.UserId == 101);
        Assert.Contains(report.AccessPatterns, access => access.ActorUserId == 102 && access.Count == 1);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reportService.GetActivityReportAsync(101));
    }
}
