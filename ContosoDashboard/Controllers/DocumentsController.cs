using System.Security.Claims;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace ContosoDashboard.Controllers;

[ApiController]
[Authorize]
[Route("api/documents")]
public sealed class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documents;
    private readonly IFileStorageService _storage;
    private readonly ILogger<DocumentsController> _logger;
    public DocumentsController(IDocumentService documents, DocumentAuthorizationService auth, IFileStorageService storage,
        ILogger<DocumentsController>? logger = null)
    { _documents = documents; _storage = storage; _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DocumentsController>.Instance; }
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("upload")]
    [RequestSizeLimit(26214400 * 20)]
    [RequestFormLimits(MultipartBodyLengthLimit = 26214400 * 20)]
    public async Task<IActionResult> Upload([FromForm] string title, [FromForm] string? description,
        [FromForm] string category, [FromForm] int? projectId, [FromForm] int? taskId, [FromForm] string[]? tags,
        [FromForm] IFormFileCollection files)
    {
        var uploaded = new List<UploadFile>();
        foreach (var file in files)
        {
            if (file.Length > DocumentValidation.MaxFileSizeBytes)
            {
                uploaded.Add(new UploadFile(Array.Empty<byte>(), file.FileName, file.ContentType, file.Length));
                continue;
            }
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            uploaded.Add(new UploadFile(ms.ToArray(), file.FileName, file.ContentType, file.Length));
        }
        var results = await _documents.UploadAsync(ActorId, new UploadRequest(title, description, category, projectId, taskId, tags), uploaded);
        return Ok(new { results });
    }

    [HttpGet("mine")]     public async Task<IActionResult> Mine([FromQuery] string? sortBy, [FromQuery] string? category,
        [FromQuery] int? projectId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _documents.MineAsync(ActorId, sortBy, category, projectId, from, to));
    [HttpGet("shared")] public async Task<IActionResult> Shared() => Ok(await _documents.SharedAsync(ActorId));
    [HttpGet("project/{projectId:int}")] public async Task<IActionResult> Project(int projectId) => Ok(await _documents.ProjectAsync(ActorId, projectId));
    [HttpGet("task/{taskId:int}")] public async Task<IActionResult> TaskDocuments(int taskId) => Ok(await _documents.TaskAsync(ActorId, taskId));
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] string? category,
        [FromQuery] int? projectId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _documents.SearchAsync(ActorId, q, category, projectId, from, to));

    [HttpGet("{documentId:int}/download")]
    public async Task<IActionResult> Download(int documentId)
    {
        try
        {
            var document = await _documents.GetAsync(ActorId, documentId);
            if (document is null || document.ScanStatus != ScanStatus.Available) return NotFound();
            var stream = await _storage.OpenReadAsync(document.StorageKey);
            if (stream is null) return NotFound();
            await _documents.RecordDownloadAsync(ActorId, documentId);
            var provider = new FileExtensionContentTypeProvider();
            provider.TryGetContentType(document.OriginalFileName, out var contentType);
            contentType ??= document.OriginalFileType;
            return File(stream, contentType, Path.GetFileName(document.OriginalFileName));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document download failed for document {DocumentId}", documentId);
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Document download failed");
        }
    }

    [HttpGet("{documentId:int}/preview")]
    public async Task<IActionResult> Preview(int documentId)
    {
        var document = await _documents.GetAsync(ActorId, documentId);
        if (document is null || document.ScanStatus != ScanStatus.Available) return NotFound();
        if (!new[] { "PDF", "JPEG", "PNG" }.Contains(document.SupportedFileType))
            return StatusCode(406);
        var stream = await _storage.OpenReadAsync(document.StorageKey);
        if (stream is null) return NotFound();
        await _documents.RecordDownloadAsync(ActorId, documentId);
        return File(stream, document.OriginalFileType, enableRangeProcessing: true);
    }

    [HttpPut("{documentId:int}")]
    public async Task<IActionResult> Update(int documentId, [FromBody] MetadataUpdateRequest request)
    {
        var ok = await _documents.UpdateAsync(ActorId, documentId, request.Title, request.Description, request.Category, request.ProjectId, request.Tags);
        return ok ? Ok() : Forbid();
    }
    [HttpPut("{documentId:int}/replacement")]
    [RequestSizeLimit(26214400)]
    [RequestFormLimits(MultipartBodyLengthLimit = 26214400)]
    public async Task<IActionResult> Replacement(int documentId, IFormFile file)
    {
        if (file.Length > DocumentValidation.MaxFileSizeBytes)
            return BadRequest(new { message = "Replacement files must be no larger than 25 MB." });
        await using var ms = new MemoryStream(); await file.CopyToAsync(ms);
        var ok = await _documents.ReplaceAsync(ActorId, documentId, new UploadFile(ms.ToArray(), file.FileName, file.ContentType, file.Length));
        return ok ? Ok() : Forbid();
    }
    [HttpDelete("{documentId:int}")]
    public async Task<IActionResult> Delete(int documentId) => await _documents.DeleteAsync(ActorId, documentId) ? NoContent() : Forbid();

    [HttpPost("{documentId:int}/shares")]
    public async Task<IActionResult> AddShare(int documentId, [FromBody] ShareRequest request)
    {
        var ok = await _documents.AddShareAsync(ActorId, documentId, request.TargetUserId, request.TargetDepartment);
        return ok ? Ok() : BadRequest();
    }
    [HttpDelete("{documentId:int}/shares/{shareId:int}")]
    public async Task<IActionResult> RemoveShare(int documentId, int shareId)
        => await _documents.RemoveShareAsync(ActorId, documentId, shareId) ? NoContent() : Forbid();

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var (count, latest) = await _documents.DashboardAsync(ActorId);
        return Ok(new { count, latest });
    }
}

public sealed record MetadataUpdateRequest(string Title, string? Description, string Category, int? ProjectId, IReadOnlyCollection<string>? Tags);
public sealed record ShareRequest(int? TargetUserId, string? TargetDepartment);
