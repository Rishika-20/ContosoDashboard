using System.Security.Claims;
using ContosoDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ContosoDashboard.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/admin/documents")]
public sealed class AdminDocumentsController : ControllerBase
{
    private readonly IDocumentService _documents;
    private readonly DocumentReportService _reports;
    public AdminDocumentsController(IDocumentService documents, DocumentReportService reports)
    { _documents = documents; _reports = reports; }
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("{documentId:int}/scan-retry")]
    public async Task<IActionResult> Retry(int documentId)
        => await _documents.RetryQuarantineAsync(ActorId, documentId) ? Ok() : Forbid();
    [HttpDelete("{documentId:int}/quarantine")]
    public async Task<IActionResult> Purge(int documentId)
        => await _documents.PurgeQuarantineAsync(ActorId, documentId) ? NoContent() : Forbid();
    [HttpGet("reports/activity")]
    public async Task<IActionResult> Report()
    {
        try { return Ok(await _reports.GetActivityReportAsync(ActorId)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }
}
