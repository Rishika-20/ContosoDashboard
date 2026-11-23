# Document Interface Contract

This feature is implemented in a Blazor Server application. These contracts define protected operations, expected authorization failures, and response shapes for future controller endpoints and service calls.

## Batch Upload

`POST /api/documents/upload`

Multipart fields:

- `files[]`: browser files
- `title`: required string
- `description`: optional string
- `category`: required predefined category
- `projectId`: optional integer
- `taskId`: optional integer
- `tags[]`: optional tags

Behavior:

- Authenticate before validation.
- Process every file independently.
- Do not re-upload successful files during retry.
- Return one result per file.

Response:

```json
{
  "results": [
    {
      "originalFileName": "plan.pdf",
      "documentId": 42,
      "status": "Available",
      "success": true,
      "message": "Uploaded"
    },
    {
      "originalFileName": "malware.exe",
      "documentId": null,
      "status": "Rejected",
      "success": false,
      "message": "Unsupported file type"
    }
  ]
}
```

For files submitted for asynchronous scan, `status` is initially `"Pending"` and the upload response reports that the file is safely held until scan processing completes. The web application enqueues a `document-scans` Queue Storage message after metadata and file storage succeed. The Azure Functions Queue Storage trigger returns no direct response to the browser; subsequent list/detail calls show the final state when processing succeeds or fails.

Authorization: Employee or higher may upload personal/team content; project/task association additionally requires active project membership or explicit separately authorized exception.

## Metadata Update

`PUT /api/documents/{documentId}`

Owner-only update of title, description, category, project association, and tags. Project reassociation requires active membership in the target project or explicit exception.

Failure:

- `404` for unknown or inaccessible documents
- `403` for non-owner
- `400` for invalid category or metadata

## File Replacement

`PUT /api/documents/{documentId}/replacement`

Multipart fields:

- `file`: replacement bytes

Behavior:

- Owner-only.
- Preserve `DocumentId`, existing metadata, and old file until the new file is stored and record update succeeds.
- Return no success if storage or metadata update fails.

## Delete

`DELETE /api/documents/{documentId}`

Owner-only permanent deletion. Project membership or project-manager role alone does not grant deletion.

## Authorized Download

`GET /api/documents/{documentId}/download`

- Requires owner, active project membership for an associated project, or direct/department share.
- Available state only.
- Streams bytes outside `wwwroot`.
- Sets safe filename and original MIME type.
- Records download activity.

## Authorized Preview

`GET /api/documents/{documentId}/preview`

- Uses the same authorization and availability checks as download.
- Supports PDF/JPEG/PNG inline preview.
- Unsupported content types return `406` or direct the caller to download.

## Search and Lists

- `GET /api/documents/mine`
- `GET /api/documents/shared`
- `GET /api/documents/project/{projectId}`
- `GET /api/documents/search?q=...&category=...&projectId=...&from=...&to=...`

All list/search responses contain only available documents the current user may access. Project and shared list endpoints return `403` or an empty unauthorized set according to UI route conventions; document service operations must never disclose inaccessible IDs or metadata.

## Shares

`POST /api/documents/{documentId}/shares`

```json
{
  "targetUserId": 4,
  "targetDepartment": null
}
```

Exactly one target is required. Owner-only. Team/department membership is resolved on every authorization check.

`DELETE /api/documents/{documentId}/shares/{shareId}`

Owner-only.

## Quarantine Administration

`POST /api/admin/documents/{documentId}/scan-retry`  
`DELETE /api/admin/documents/{documentId}/quarantine`

Administrator-only. Retry creates a new queue message and scan attempt with a new correlation ID; the existing file remains inaccessible until the retry succeeds. Purge permanently removes quarantine bytes and metadata according to the existing permanent-delete policy. Queue retry attempts must not make poisoned or pending files available.

## Dashboard and Reports

- `GET /api/documents/dashboard`: latest five available documents uploaded by current user and document count.
- `GET /api/admin/documents/reports/activity`: administrator-only aggregate report for file types, active uploaders, and access patterns.
