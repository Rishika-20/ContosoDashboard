using System.Security.Claims;
using ContosoDashboard.Controllers;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestFixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ContosoDashboard.Tests.Controllers;

public class DocumentsControllerTests
{
    [Fact]
    public async Task Pending_file_is_not_downloadable_even_by_owner()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.AddDocumentAsync(scanStatus: ScanStatus.Pending);
        var controller = CreateController(env, 101);

        var result = await controller.Download(1);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Available_file_download_requires_an_authorized_grant()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.AddDocumentAsync();
        var controller = CreateController(env, 103);

        var denied = await controller.Download(1);
        Assert.IsType<NotFoundResult>(denied);

        env.Db.DocumentShares.Add(new DocumentShare { DocumentId = 1, TargetUserId = 103, GrantedByUserId = 101 });
        await env.Db.SaveChangesAsync();
        var granted = await controller.Download(1);
        Assert.IsType<FileStreamResult>(granted);
    }

    [Fact]
    public async Task Project_members_and_current_department_share_recipients_can_download()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.AddDocumentAsync(projectId: 501);
        var memberResult = await CreateController(env, 102).Download(1);
        Assert.IsType<FileStreamResult>(memberResult);

        var personal = await env.AddDocumentAsync(id: 2, storageKey: "f9be1ec3634f4cf19c987ca8d3648c79.pdf");
        env.Db.DocumentShares.Add(new DocumentShare
        {
            DocumentId = personal.DocumentId, TargetDepartment = "Finance", GrantedByUserId = 101
        });
        await env.Db.SaveChangesAsync();
        var teamResult = await CreateController(env, 103).Download(2);
        Assert.IsType<FileStreamResult>(teamResult);
    }

    [Fact]
    public async Task Preview_is_inline_for_supported_types_and_rejects_office_files()
    {
        await using var env = new DocumentServiceTestEnvironment();
        await env.SeedAsync();
        await env.AddDocumentAsync();
        var controller = CreateController(env, 101);

        Assert.IsType<FileStreamResult>(await controller.Preview(1));
        var document = await env.Db.Documents.FindAsync(1);
        document!.SupportedFileType = "DOCX";
        document.OriginalFileName = "report.docx";
        await env.Db.SaveChangesAsync();

        var unsupported = await controller.Preview(1);
        Assert.Equal(StatusCodes.Status406NotAcceptable, Assert.IsType<StatusCodeResult>(unsupported).StatusCode);
    }

    private static DocumentsController CreateController(DocumentServiceTestEnvironment env, int actorId)
    {
        var controller = new DocumentsController(env.Service, new DocumentAuthorizationService(env.Db), env.Storage)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, actorId.ToString())
                    }, "Test"))
                }
            }
        };
        return controller;
    }
}
