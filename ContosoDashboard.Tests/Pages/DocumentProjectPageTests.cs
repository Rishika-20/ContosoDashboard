using ContosoDashboard.Models;
using ContosoDashboard.Tests.TestFixtures;

namespace ContosoDashboard.Tests.Pages;

public class DocumentProjectPageTests
{
    [Fact]
    public async Task Project_document_list_shows_available_documents_to_members_only()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.AddDocumentAsync(projectId: 501);

        Assert.Single(await env.Service.ProjectAsync(102, 501));
        Assert.Empty(await env.Service.ProjectAsync(103, 501));

        var pending = await env.AddDocumentAsync(id: 2, projectId: 501,
            scanStatus: ScanStatus.Pending, storageKey: "f9be1ec3634f4cf19c987ca8d3648c79.pdf");
        Assert.Equal(ScanStatus.Pending, pending.ScanStatus);
        Assert.Single(await env.Service.ProjectAsync(102, 501));
    }
}
