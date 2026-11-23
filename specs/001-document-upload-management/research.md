# Phase 0 Research: Document Upload and Management

## Decision: Use the existing ASP.NET Core/Blazor Server architecture

- **Rationale:** The repository already uses .NET 10, Blazor Server, EF Core SQL Server, cookie mock authentication, Bootstrap, services, and Razor Pages. Feature-specific UI and services fit this structure without a framework rewrite.
- **Alternatives considered:** Separate SPA/API project was rejected as unnecessary; direct filesystem access from Blazor was rejected because authorization and streaming belong in protected server endpoints/services.

## Decision: Keep EF Core metadata independent from the storage provider

- **Rationale:** Existing SQL Server metadata and integer keys match the repository and specification. Store a relative GUID-based storage key, with provider details hidden behind `IFileStorageService`.
- **Alternatives considered:** Absolute filesystem paths were rejected due to migration and portability concerns; storing blobs in SQL was rejected for simplicity and as inconsistent with the planned storage abstraction.

## Decision: Add an `IFileStorageService` and `LocalFileStorageService`

- **Rationale:** Local files outside `wwwroot` satisfy offline operation and prevent unauthenticated static serving. A small interface supports cloud migration without changing document business rules.
- **Alternatives considered:** Azure SDK integration was rejected because it is cloud-specific and contradicts the offline design.

## Decision: Use a replaceable malware scanner boundary

- **Rationale:** The feature requires no unscanned file to become available. `IMalwareScanner` can be implemented by a testable local/training scanner while preserving the production swap point.
- **Alternatives considered:** Running no scan was rejected by the specification; placing scan logic in UI was rejected because it would be bypassable.
- **State behavior:** `Pending` is used if scanning is unavailable. `Available` requires successful scan. `Quarantined` is retained for 30 days for administrator retry/purge.

## Decision: Process malware scans with an Azure Functions Queue Storage trigger

- **Rationale:** Uploading must not wait for an external security product or block the Blazor request. A separate Azure Functions worker can consume `document-scans` messages, load the stored bytes, invoke `IMalwareScanner`, and update scan state while the web application remains usable.
- **Production flow:** Web application writes metadata and the file, creates `ScanStatus.Pending`, and enqueues `{DocumentId, StorageKey, Attempt, CorrelationId}`. The `document-scans` Queue Storage trigger loads and scans the file and records `Available` or `Quarantined`. Scanner infrastructure failures remain pending and rely on bounded Queue Storage retries.
- **Reliability:** Queue messages contain no file bytes or secrets, and processing is idempotent by document/correlation pair. Repeated transient failures are moved to a poison queue. Administrator retry creates a new job and an explicit scan activity record.
- **Offline/local alternative:** Azurite Queue Storage plus the Functions host can run the trigger locally. If Azure infrastructure is unavailable, a testable local background worker/scanner may process pending jobs, but no pending or quarantined file is exposed.
- **Alternatives considered:** Synchronous scanning was rejected because it can exceed upload latency and tie the request to scanner availability. Blob-trigger-only processing was rejected because the queue payload is more portable and supports replayable, auditable retries. Executing a process directly in the web app was rejected because it does not scale independently and couples scanning to web lifecycle.

## Decision: Resolve team shares against current department membership

- **Rationale:** The existing `User.Department` claim/property is the available team/department grouping. A department share stores a department key and authorization queries current users/departments for each decision. Team membership changes therefore take effect immediately.
- **Alternatives considered:** A role-based team share was rejected because it could broaden access. A frozen membership snapshot was rejected because changes must be dynamically reflected.
- **Required mock-auth change:** The login flow must include `ClaimTypes.Department` or an application-specific department claim so team/department claims remain available consistently.

## Decision: Apply project membership to every project document check

- **Rationale:** Project managers, team leads, and project membership are distinct in the clarified rules. Project document access requires current `ProjectMember` membership or an explicit share/owner exception. Project manager status alone does not grant access or upload/deletion authority.
- **Alternatives considered:** Reusing existing role policies was rejected because role-only policies would overgrant access.

## Decision: Implement batch uploads with independent file transactions

- **Rationale:** Each file needs independent validation, storage, scan, metadata, and result feedback. The UI tracks each item and removes successful items from the retry set.
- **Alternatives considered:** All-or-nothing upload was rejected by the partial-success requirement.

## Decision: Implement replacement with compensate-and-retain behavior

- **Rationale:** Store the new GUID-keyed file first, update the existing record atomically, and delete the old file only afterward. If record update fails, delete the new file and retain the old record/key. If storage fails, no metadata change occurs.
- **Alternatives considered:** Replacing or overwriting the old storage key was rejected because a failed update could destroy the available old file.

## Decision: Treat task upload as project-inherited upload

- **Rationale:** `TaskItem.ProjectId` supplies the project association. Service authorization requires current project membership before accepting the upload; the UI does not determine authority.
- **Alternatives considered:** Allowing project manager role only was rejected. Allowing any task assignee was rejected unless separately backed by project membership or an explicit exception.

## Decision: Add a test project only in implementation planning

- **Rationale:** Test-first behavior is required by the constitution, but no test project exists in the repository. The implementation plan requires focused service/auth/storage tests with EF Core test provider support.
- **Alternatives considered:** UI-only testing was rejected because authorization and storage invariants require service-level coverage.
