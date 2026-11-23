# Implementation Plan: Document Upload and Management

**Branch**: `Not assigned` | **Date**: 2026-11-23 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-document-upload-management/spec.md`

## Summary

Add a secure document library to the existing ASP.NET Core/Blazor Server application. The implementation will extend EF Core/SQL Server LocalDB metadata, keep files outside `wwwroot`, expose downloads and previews only through authorized application endpoints, and use replaceable storage and malware-scanner interfaces. Authorization remains service-level and view-independent: ownership grants maintenance, active project membership grants project document access, direct user/team shares grant only explicit access, and role or project-manager status does not confer project-document upload or deletion authority.

## Technical Context

**Language/Version**: C#, .NET 10 (repository currently targets net10.0)  
**Primary Dependencies**: ASP.NET Core, Blazor Server, Razor Pages, EF Core 10 SQL Server, Bootstrap/Bootstrap Icons  
**Storage**: SQL Server LocalDB metadata; local filesystem under `AppData/uploads`, outside `wwwroot`  
**Testing**: No test project exists yet; implementation requires a focused test project using EF Core InMemory or SQLite plus service/unit tests  
**Target Platform**: Windows local training deployment with SQL Server LocalDB  
**Project Type**: Single ASP.NET Core/Blazor Server application  
**Performance Goals**: 25 MB upload within 30 seconds; lists up to 500 documents within 2 seconds; authorized search within 2 seconds; preview within 3 seconds  
**Constraints**: Offline/local operation; mock cookies and claims; malware scan must complete successfully before exposure; Azure Queue Storage and Azure Functions are optional production scan infrastructure; no secrets or cloud SDK required for local training mode
**Scale/Scope**: Training-scale users and projects; approximately 500-document lists

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Status | Evidence |
|---|---|---|
| Test-first behavior | PASS | Add focused service, authorization, storage, and UI-state tests before implementation |
| Maintainable layered structure | PASS | Models/Data/Services/Pages plus replaceable infrastructure boundaries |
| Authentication and least privilege | PASS | Claims-based identity and service checks on every operation; no role-only project authority |
| Observability | PASS | Structured logs and audit activity records; no file content or secrets |
| Simplicity/YAGNI | PASS | Reuse existing EF Core, cookie auth, Razor/Blazor, and notification patterns |
| Offline-first | PASS | Local filesystem and LocalDB; interfaces do not require external providers |
| Technical stack | PASS | .NET 10, Blazor Server, EF Core SQL Server, Bootstrap |

No unresolved Constitution gate violations are planned.

## Project Structure

### Documentation (this feature)

```text
specs/001-document-upload-management/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── document-api.md
└── checklists/
    └── requirements.md
```

### Source Code (repository root)

```text
ContosoDashboard/
├── Data/
│   └── ApplicationDbContext.cs
├── Models/
│   ├── Document.cs
│   ├── DocumentShare.cs
│   ├── DocumentActivity.cs
│   ├── DocumentScanAttempt.cs
│   └── existing project/task/user models
├── Services/
│   ├── DocumentService.cs
│   ├── DocumentAuthorizationService.cs
│   ├── IFileStorageService.cs
│   ├── LocalFileStorageService.cs
│   ├── IMalwareScanner.cs
│   ├── TestableMalwareScanner.cs
│   └── existing services
├── Pages/
│   ├── Documents.razor
│   ├── DocumentDetails.razor
│   ├── Login.cshtml.cs
│   └── existing pages
└── Controllers/
    └── DocumentsController.cs
DocumentScanner.Function/
├── host.json
├── local.settings.json.example
└── Functions/
    └── ProcessDocumentScan.cs
```

**Structure Decision**: Extend the existing single application. Use a domain-focused `DocumentService`, isolated authorization logic, `IFileStorageService` and `IMalwareScanner` infrastructure boundaries, a Blazor page for management, and controller endpoints for authorized streaming, preview, and download. Add a separate Azure Functions project for production scan processing.

## Architecture and Implementation Plan

### 1. Metadata and persistence

Add integer-keyed EF Core entities for `Document`, `DocumentShare`, `DocumentTag`, `DocumentActivity`, and `DocumentScanAttempt`. Store relative storage keys rather than absolute paths. Existing `Project`, `TaskItem`, and `ProjectMember` entities remain the membership sources. Preserve the `DocumentId` through metadata edits and file replacement.

Use indexed title/category/uploader/project fields and explicit indexes for list and search queries. Because this repository currently uses `EnsureCreated()`, the implementation must update database initialization intentionally and document LocalDB reset/recreation only for development test setup.

### 2. Upload and quarantine workflow

1. Validate authenticated user and title/category.
2. Validate every file independently for allowlisted extension/MIME pair and 25 MB limit.
3. Copy each browser stream to memory using the existing Blazor `IBrowserFile` pattern.
4. Generate GUID-based storage key before persistence.
5. Upload bytes to the storage provider.
6. Commit metadata with `ScanStatus.Pending` and no availability.
7. Enqueue one queue message containing `DocumentId`, storage key/reference, retry attempt, and correlation ID.
8. A separate Azure Functions app processes `document-scans` with a Queue Storage trigger.
9. The function loads the file through the shared storage-provider interface, invokes `IMalwareScanner`, and records the result through the application data boundary.
10. On success, transition to `Available`; on a confirmed malware/signature failure, transition to `Quarantined`; on scanner/transient infrastructure failure, return without exposing the file and allow Queue Storage retry.
11. Preserve partial-success results for batch uploads; the UI retains only failed items for retry.
12. On storage/database failure, leave no exposed document and clean up newly stored bytes when metadata or subsequent work fails.

#### Azure Functions scan job

- Queue: `document-scans`
- Trigger: Azure Functions Queue Storage trigger on `AzureWebJobsStorage`.
- Message payload: `DocumentId`, `StorageKey`, `Attempt`, and `CorrelationId`; no file bytes or user secrets.
- Poison handling: configure bounded retry/dequeue behavior and route repeated infrastructure failures to `document-scans-poison`.
- Result handling: the function updates document scan state, quarantine details, and `DocumentActivity` through the application/service boundary.
- Idempotency: processing is keyed by `DocumentId` plus `CorrelationId`; a successful or quarantined document is not scanned again unless an administrator explicitly requests retry.
- Local mode: Azurite Queue Storage and the Functions host may run locally for development; if Azure infrastructure is unavailable, the testable local worker/scanner may process pending jobs and files remain pending until a successful result is recorded.
- Security: use managed identity/workload identity for Azure resources where applicable; keep connection strings and scanner credentials outside source control and out of queue messages.

Pending and quarantined files remain inaccessible. Quarantine lasts 30 days, with administrator retry or permanent purge operations only.

### 3. Replacement

Replacement uploads a new GUID-keyed file first. Only after storage success is the document metadata and storage key committed in-place. The database transaction/update and file swap must tolerate failure: if the update fails, delete the new file and retain the old key/file. Delete the old file only after the record has been successfully updated and the new file is available. The document ID and metadata remain unchanged.

### 4. Authorization

Implement all authorization in `DocumentAuthorizationService` and enforce again in `DocumentService` and controller endpoints:

- Owner: view, edit, replace, share, and delete.
- Active project member: view/download/upload project documents only for explicitly associated projects.
- Existing project managers do not gain project-document authority solely by role; project membership remains required for project access.
- Direct user share: access for the named user.
- Department/team share: access recalculated from current `User.Department` on every authorization decision.
- Administrator: audit/report/quarantine administration only; ordinary viewing/maintenance still follows the feature authorization model unless a separately authorized exception exists.
- TeamLead role does not confer cross-project access.

### 5. Preview, download, and delete

Controller endpoints will resolve the current user, verify document availability and authorization, stream local bytes or provider streams, set safe disposition/content type, and log download activity. PDF/JPEG/PNG preview uses inline content disposition only where the browser is supported; Office/text use download. Deletion verifies owner identity and atomically removes metadata/listing and storage, logging a permanent deletion without file content.

### 6. Team/task/dashboard integration

- Project detail view lists project documents and permits upload by active project members.
- Task association resolves `TaskItem.ProjectId`; task detail/attachment UI requires task access plus project membership or another grant.
- Dashboard gets five latest available documents uploaded by the current user and user's document count.
- Existing `NotificationType` and `NotificationService` patterns are extended for share and project-document notifications.
- Dashboard, project, task, and shared views filter out unavailable and unauthorized documents.

### 7. Reporting and audit

Add document activity for upload, download, delete, share, metadata change, scan, replacement, quarantine retry/purge. Activity stores actor, document, optional project/recipient, event type/time, and outcomes. It does not store file content. Report queries aggregate file types, active uploaders, and access patterns for administrators.

## Complexity Tracking

No constitution violation or complexity exception is required.

## Phase 0 Research Outcomes

See [research.md](./research.md). Open decisions are resolved before implementation:

- Local first and storage interface retained for provider swaps
- EF Core metadata with GUID-backed relative storage keys
- Testable scanner boundary with Pending/Available/Quarantined states
- Azure Functions Queue Storage trigger for asynchronous scan processing
- Team shares resolved from current department membership
- Blazor batch state with per-file independent outcomes
- Atomic replacement with compensate-and-retain behavior

## Phase 1 Design Outputs

- [data-model.md](./data-model.md)
- [document-api.md](./contracts/document-api.md)
- [quickstart.md](./quickstart.md)
