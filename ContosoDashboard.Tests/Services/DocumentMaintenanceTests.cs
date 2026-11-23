using ContosoDashboard.Models;
using ContosoDashboard.Tests.TestFixtures;

namespace ContosoDashboard.Tests.Services;

public class DocumentMaintenanceTests
{
    [Fact]
    public async Task Metadata_changes_are_owner_only_and_preserve_document_id()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.AddDocumentAsync();

        Assert.False(await env.Service.UpdateAsync(102, 1, "Changed", null, "General", null, null));
        Assert.True(await env.Service.UpdateAsync(101, 1, "Changed", "Description", "Legal", null, new[] { "Policy" }));

        var saved = await env.Db.Documents.FindAsync(1);
        Assert.Equal(1, saved!.DocumentId);
        Assert.Equal("Changed", saved.Title);
        Assert.Equal("policy", Assert.Single(env.Db.DocumentTags).Name);
        Assert.Contains(env.Db.DocumentActivities, a => a.ActivityType == DocumentActivityType.MetadataChange);
    }

    [Fact]
    public async Task Share_target_must_be_exactly_one_and_delete_is_owner_only()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.AddDocumentAsync();

        Assert.False(await env.Service.AddShareAsync(101, 1, 102, "Engineering"));
        Assert.False(await env.Service.AddShareAsync(101, 1, null, null));
        Assert.False(await env.Service.DeleteAsync(102, 1));
        Assert.True(await env.Service.DeleteAsync(101, 1));
        Assert.Empty(env.Db.Documents);
        Assert.Contains(env.Db.DocumentActivities, a => a.ActivityType == DocumentActivityType.Delete);
    }

    [Fact]
    public async Task Quarantined_owner_cannot_delete_or_maintain_document()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.AddDocumentAsync(scanStatus: ScanStatus.Quarantined);

        Assert.False(await env.Service.DeleteAsync(101, 1));
        Assert.False(await env.Service.UpdateAsync(101, 1, "Changed", null, "General", null, null));
        Assert.Single(env.Db.Documents);
    }

    [Fact]
    public async Task Storage_delete_failure_preserves_document_metadata()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.AddDocumentAsync();
        env.Storage.FailDelete = true;

        Assert.False(await env.Service.DeleteAsync(101, 1));
        Assert.Single(env.Db.Documents);
    }

    [Fact]
    public async Task Only_administrator_can_retry_or_purge_quarantine()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.AddDocumentAsync(id: 1, scanStatus: ScanStatus.Quarantined);
        await env.AddDocumentAsync(id: 2, scanStatus: ScanStatus.Quarantined,
            storageKey: "f9be1ec3634f4cf19c987ca8d3648c79.pdf");

        Assert.False(await env.Service.RetryQuarantineAsync(101, 1));
        Assert.True(await env.Service.RetryQuarantineAsync(104, 1));
        Assert.Equal(ScanStatus.Pending, (await env.Db.Documents.FindAsync(1))!.ScanStatus);
        Assert.Single(env.Queue.Messages);

        Assert.False(await env.Service.PurgeQuarantineAsync(101, 2));
        Assert.True(await env.Service.PurgeQuarantineAsync(104, 2));
        Assert.Null(await env.Db.Documents.FindAsync(2));
        Assert.Contains(env.Db.DocumentActivities, a => a.ActivityType == DocumentActivityType.Purge && a.DocumentId is null);
    }
}
