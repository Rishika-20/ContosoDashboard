using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestFixtures;

namespace ContosoDashboard.Tests.Services;

public class DocumentSharingTests
{
    [Fact]
    public async Task Department_share_grants_current_members_and_notifies_them()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.AddDocumentAsync();

        Assert.True(await env.Service.AddShareAsync(101, 1, null, "Engineering"));
        Assert.True(await new DocumentAuthorizationService(env.Db).CanViewAsync(102, 1));
        Assert.Contains(env.Notifications.Created, n => n.UserId == 102 && n.Type == NotificationType.DocumentShare);

        var member = await env.Db.Users.FindAsync(102);
        member!.Department = "Finance";
        await env.Db.SaveChangesAsync();
        Assert.False(await new DocumentAuthorizationService(env.Db).CanViewAsync(102, 1));
    }
}
