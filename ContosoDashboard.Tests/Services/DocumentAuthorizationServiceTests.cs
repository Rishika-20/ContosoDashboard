using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestFixtures;

namespace ContosoDashboard.Tests.Services;

public class DocumentAuthorizationServiceTests
{
    [Fact]
    public async Task Owner_can_maintain_and_view_available_document()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.AddDocumentAsync();
        var auth = new DocumentAuthorizationService(env.Db);

        Assert.True(await auth.CanMaintainAsync(101, 1));
        Assert.True(await auth.CanViewAsync(101, 1));
        Assert.False(await auth.CanMaintainAsync(102, 1));
    }

    [Fact]
    public async Task Current_project_membership_grants_view_but_role_alone_does_not()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.AddDocumentAsync(projectId: 501);
        var auth = new DocumentAuthorizationService(env.Db);

        Assert.True(await auth.CanViewAsync(102, 1));
        Assert.False(await auth.CanViewAsync(103, 1)); // ProjectManager role without membership
        Assert.False(await auth.CanUploadProjectAsync(103, 501));
    }

    [Fact]
    public async Task Direct_and_department_shares_are_resolved_against_current_user_data()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.AddDocumentAsync();
        env.Db.DocumentShares.AddRange(
            new DocumentShare { DocumentId = 1, TargetUserId = 103, GrantedByUserId = 101 },
            new DocumentShare { DocumentId = 1, TargetDepartment = "Engineering", GrantedByUserId = 101 });
        await env.Db.SaveChangesAsync();
        var auth = new DocumentAuthorizationService(env.Db);

        Assert.True(await auth.CanViewAsync(103, 1));
        Assert.True(await auth.CanViewAsync(102, 1));

        var member = await env.Db.Users.FindAsync(102);
        member!.Department = "Finance";
        await env.Db.SaveChangesAsync();
        Assert.False(await auth.CanViewAsync(102, 1));
    }

    [Fact]
    public async Task Pending_and_quarantined_documents_cannot_be_opened()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.AddDocumentAsync(scanStatus: ScanStatus.Pending);
        var auth = new DocumentAuthorizationService(env.Db);

        Assert.False(await auth.CanViewAsync(101, 1));
        Assert.True(await auth.CanInspectScanAsync(101, 1));
    }
}
