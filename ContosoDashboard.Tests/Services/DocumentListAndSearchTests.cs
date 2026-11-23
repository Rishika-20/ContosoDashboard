using ContosoDashboard.Models;
using ContosoDashboard.Tests.TestFixtures;

namespace ContosoDashboard.Tests.Services;

public class DocumentListAndSearchTests
{
    [Fact]
    public async Task Mine_list_supports_sorting_and_filters()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.AddDocumentAsync(id: 1, title: "Zulu");
        await env.AddDocumentAsync(id: 2, title: "Alpha", storageKey: "f9be1ec3634f4cf19c987ca8d3648c79.pdf");
        await env.AddDocumentAsync(id: 3, title: "Project file", projectId: 501, storageKey: "f9be1ec3634f4cf19c987ca8d3648c80.pdf");

        var sorted = await env.Service.MineAsync(101, "title");
        Assert.Equal(new[] { "Alpha", "Project file", "Zulu" }, sorted.Select(d => d.Title));
        var projectOnly = await env.Service.MineAsync(101, projectId: 501);
        Assert.Single(projectOnly);
        Assert.Equal(501, projectOnly[0].ProjectId);
    }

    [Fact]
    public async Task Search_matches_project_and_tag_but_excludes_inaccessible_or_unavailable_documents()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.AddDocumentAsync(id: 1, uploaderId: 101, projectId: 501, title: "Quarterly material");
        await env.Db.DocumentTags.AddAsync(new DocumentTag { DocumentId = 1, Name = "forecast" });
        await env.AddDocumentAsync(id: 2, uploaderId: 103, title: "Secret material", storageKey: "f9be1ec3634f4cf19c987ca8d3648c79.pdf");
        await env.AddDocumentAsync(id: 3, uploaderId: 101, title: "Pending material", scanStatus: ScanStatus.Pending, storageKey: "f9be1ec3634f4cf19c987ca8d3648c80.pdf");
        env.Db.DocumentShares.Add(new DocumentShare { DocumentId = 2, TargetUserId = 102, GrantedByUserId = 103 });
        await env.Db.SaveChangesAsync();

        var byProject = await env.Service.SearchAsync(102, "Project Alpha", null, null, null, null);
        var byTag = await env.Service.SearchAsync(102, "forecast", null, null, null, null);
        var byShare = await env.Service.SearchAsync(102, "Secret material", null, null, null, null);
        var pending = await env.Service.SearchAsync(101, "Pending material", null, null, null, null);

        Assert.Contains(byProject, d => d.DocumentId == 1);
        Assert.Contains(byTag, d => d.DocumentId == 1);
        Assert.Contains(byShare, d => d.DocumentId == 2);
        Assert.Empty(pending);
    }
}
