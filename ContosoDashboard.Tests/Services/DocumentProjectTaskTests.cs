using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestFixtures;

namespace ContosoDashboard.Tests.Services;

public class DocumentProjectTaskTests
{
    [Fact]
    public async Task Task_upload_inherits_project_and_requires_current_membership()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        env.Db.Tasks.Add(new TaskItem
        {
            TaskId = 701, Title = "Task", AssignedUserId = 102, CreatedByUserId = 101,
            ProjectId = 501, Status = Models.TaskStatus.InProgress
        });
        await env.Db.SaveChangesAsync();

        var memberResult = await env.Service.UploadAsync(102,
            new UploadRequest("Task attachment", null, "Project", null, 701, null),
            new[] { new UploadFile(new byte[] { 1 }, "task.pdf", "application/pdf") });
        var memberDocument = Assert.Single(env.Db.Documents);
        Assert.True(memberResult.Single().Success);
        Assert.Equal(501, memberDocument.ProjectId);
        Assert.Equal(701, memberDocument.TaskId);

        var denied = await env.Service.UploadAsync(103,
            new UploadRequest("Role-only upload", null, "Project", null, 701, null),
            new[] { new UploadFile(new byte[] { 1 }, "denied.pdf", "application/pdf") });
        Assert.False(denied.Single().Success);
        Assert.Single(env.Db.Documents);
    }

    [Fact]
    public async Task Explicit_share_is_independent_of_project_membership()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.AddDocumentAsync(projectId: 501);
        env.Db.DocumentShares.Add(new DocumentShare { DocumentId = 1, TargetUserId = 103, GrantedByUserId = 101 });
        await env.Db.SaveChangesAsync();

        Assert.True(await new DocumentAuthorizationService(env.Db).CanViewAsync(103, 1));
        Assert.Empty(await env.Service.ProjectAsync(103, 501));
    }

    [Fact]
    public async Task Project_upload_notifies_other_active_members()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();

        var result = await env.Service.UploadAsync(101,
            new UploadRequest("Team file", null, "Project", 501, null, null),
            new[] { new UploadFile(new byte[] { 1 }, "team.pdf", "application/pdf") });

        Assert.True(result.Single().Success);
        Assert.Contains(env.Notifications.Created, n => n.UserId == 102 && n.Type == NotificationType.ProjectDocument);
    }
}
