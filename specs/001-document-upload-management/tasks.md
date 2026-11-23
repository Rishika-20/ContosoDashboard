# Tasks: Document Upload and Management

**Input**: Design documents from `/specs/001-document-upload-management/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: The constitution requires test-first behavior. These tasks include focused service, authorization, storage, queue, and UI-state tests before implementation.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and feature structure

- [ ] T001 Create the test project `ContosoDashboard.Tests/ContosoDashboard.Tests.csproj` with xUnit and EF Core SQLite or InMemory test dependencies
- [ ] T002 [P] Add the Azure Functions project `DocumentScanner.Function/DocumentScanner.Function.csproj` with the isolated Queue Storage trigger and Azure Functions packages
- [ ] T003 [P] Create feature configuration sections and add `AzureWebJobsStorage`, `DocumentQueue`, and local upload settings to `ContosoDashboard/appsettings.Development.json`
- [ ] T004 [P] Create `ContosoDashboard/AppData/uploads/.gitkeep` and document local upload-directory setup in `specs/001-document-upload-management/quickstart.md`
- [ ] T005 [P] Add queue-host configuration and the queue names `document-scans` and `document-scans-poison` to `DocumentScanner.Function/host.json`
- [ ] T006 [P] Add `DocumentScanner.Function/local.settings.json.example` with Azurite storage placeholders and no credentials or secrets
- [ ] T007 [P] Register the new test project and Azure Functions project in a repository solution if one is added at `ContosoDashboard.sln`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core entities, infrastructure boundaries, database support, and authorization primitives required by every document story

**CRITICAL**: No user story work can begin until this phase is complete

- [ ] T008 [P] Create `ContosoDashboard/Models/Document.cs` with DocumentId, title `Required/trimmed`, description `string(2000)`, category `Required/string(100)`, uploader, project/task associations, and scan state fields
- [ ] T009 [P] Create `ContosoDashboard/Models/DocumentShare.cs` with exactly one of TargetUserId or TargetDepartment `string(100)` required and owner-at-grant validation
- [ ] T010 [P] Create `ContosoDashboard/Models/DocumentTag.cs` with Name `string(100), trimmed, unique per document, case-normalized`
- [ ] T011 [P] Create `ContosoDashboard/Models/DocumentActivity.cs` with append-only activity fields, actor, optional project/recipient, outcome, and no file-content data
- [ ] T012 [P] Create `ContosoDashboard/Models/DocumentScanAttempt.cs` with DocumentId, GUID CorrelationId, StorageKey, status, AttemptNumber starting at 1, timestamps, and sanitized FailureDetails
- [ ] T013 [P] Add document enums and validation rules for `ScanStatus`, file types, activity types, and attempt outcomes in `ContosoDashboard/Models/Document.cs` and related model files
- [ ] T014 Add the document entities and navigation relationships plus indexes for uploader/date, project/availability, category, title/search fields, and activity lookups to `ContosoDashboard/Data/ApplicationDbContext.cs`
- [ ] T015 Update mock login department claims in `ContosoDashboard/Pages/Login.cshtml.cs` with `ClaimTypes.Department` or an application-specific department claim for the selected user's department
- [ ] T016 [P] Define `IFileStorageService` and a GUID-relative-key upload/download/delete contract in `ContosoDashboard/Services/IFileStorageService.cs`
- [ ] T017 [P] Implement `LocalFileStorageService` outside `wwwroot` under `AppData/uploads` in `ContosoDashboard/Services/LocalFileStorageService.cs`
- [ ] T018 [P] Define `IMalwareScanner` and `ScanResult` statuses in `ContosoDashboard/Services/IMalwareScanner.cs`
- [ ] T019 [P] Implement `TestableMalwareScanner` in `ContosoDashboard/Services/TestableMalwareScanner.cs` and keep it replaceable for production scanners
- [ ] T020 Add document storage, scanner, queue producer, document, authorization, and reporting service registrations to `ContosoDashboard/Program.cs`
- [ ] T021 [P] Create queue message contracts and bounded retry configuration types in `ContosoDashboard/Services/DocumentQueueModels.cs`
- [ ] T022 Implement `IDocumentQueueService`/`DocumentQueueService` for Azure Queue Storage and a local/Azurite producer path in `ContosoDashboard/Services/`
- [ ] T023 Create `DocumentAuthorizationService` in `ContosoDashboard/Services/DocumentAuthorizationService.cs` for owner, project membership, direct share, department share, task, and administrator quarantine rules
- [ ] T024 [P] Add predefined document categories and allowlisted extension/MIME pairs plus the 25 MB limit in `ContosoDashboard/Services/DocumentValidation.cs`
- [ ] T025 [P] Add structured document logging and safe failure helpers in `ContosoDashboard/Services/DocumentLogging.cs`
- [ ] T026 [P] Add `DocumentShare`, document, tag, activity, and scan-attempt test fixtures in `ContosoDashboard.Tests/TestFixtures/DocumentTestFixtures.cs`
- [ ] T027 [P] Write failing authorization tests for ownership, project membership, role-only overgrant, direct shares, and department membership in `ContosoDashboard.Tests/Services/DocumentAuthorizationServiceTests.cs`
- [ ] T028 [P] Write failing validation/storage tests for 25 MB limit, allowlisted MIME/extension pairs, unsafe names, GUID keys, and cleanup in `ContosoDashboard.Tests/Services/DocumentStorageAndValidationTests.cs`
- [ ] T029 Run the focused test project and verify the foundational tests fail before implementation, as required by the test-first workflow

**Checkpoint**: Foundation ready; user story implementation can begin

---

## Phase 3: User Story 1 - Upload and Secure a Document (Priority: P1) 🎯 MVP

**Goal**: Authenticated employees can independently upload valid documents and receive partial-success, pending, quarantine, or rejection outcomes.

**Independent Test**: A signed-in employee uploads a valid PDF/Office file with title/category and finds it only after Available; oversize/unsupported files are rejected and malware-failed or unavailable scans remain inaccessible.

### Tests for User Story 1

- [ ] T030 [P] [US1] Write failing batch upload contract tests for independent file success/rejection and no re-upload of successful files in `ContosoDashboard.Tests/Services/DocumentUploadTests.cs`
- [ ] T031 [P] [US1] Write failing replacement/cleanup tests for upload storage failure, metadata failure, and no orphaned document in `ContosoDashboard.Tests/Services/DocumentUploadTests.cs`
- [ ] T032 [P] [US1] Write failing queue retry and poison tests for transient failure, bounded retries, poison routing, idempotency, and inaccessible pending files in `ContosoDashboard.Tests/Services/DocumentScanQueueTests.cs`
- [ ] T033 [P] [US1] Write failing Azure Functions processor tests for Available, Quarantined, transient, and repeated-correlation results in `DocumentScanner.Function.Tests/DocumentScanFunctionTests.cs`
- [ ] T034 [P] [US1] Run the US1 tests and confirm they fail before implementation

### Implementation for User Story 1

- [ ] T035 [US1] Implement `DocumentService.UploadAsync` with independent per-file validation, GUID storage key creation, partial-success results, metadata creation, and storage/metadata cleanup in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T036 [US1] Enforce required title/category, optional project/task association, description `string(2000)`, tags `string(100)` per tag, and 1..26214400-byte file-size rules in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T037 [US1] Implement queue publication after metadata and file storage succeed, with `DocumentId`, `StorageKey`, `Attempt`, and `CorrelationId` in `ContosoDashboard/Services/DocumentQueueService.cs`
- [ ] T038 [US1] Implement pending, available, quarantined, 30-day quarantine metadata, and administrator-only retry/purge state transitions in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T039 [US1] Implement `ProcessDocumentScan` with the Queue Storage trigger on `document-scans`, storage-provider loading, scanner invocation, and application scan-state boundary in `DocumentScanner.Function/Functions/ProcessDocumentScan.cs`
- [ ] T040 [US1] Implement bounded Queue Storage dequeue/retry handling, poison routing to `document-scans-poison`, and repeat-attempt safeguards in `DocumentScanner.Function/Functions/ProcessDocumentScan.cs`
- [ ] T041 [US1] Implement scan-attempt idempotency keyed by `DocumentId` plus `CorrelationId` and prevent reprocessing Available or Quarantined documents in `DocumentScanner.Function/Functions/ProcessDocumentScan.cs`
- [ ] T042 [US1] Implement Azurite/local queue settings and a testable local pending-worker fallback in `DocumentScanner.Function/` and `ContosoDashboard/Services/`
- [ ] T043 [US1] Implement `POST /api/documents/upload` in `ContosoDashboard/Controllers/DocumentsController.cs` with authentication, per-file batch responses, and the Pending/Rejected result contract
- [ ] T044 [US1] Implement upload progress and independent retry-state UI in `ContosoDashboard/Pages/Documents.razor`, keeping successful files out of the retry set
- [ ] T045 [US1] Add clear upload rejection messages for unsupported type/MIME pairs and files over 25 MB in `ContosoDashboard/Pages/Documents.razor`
- [ ] T046 [US1] Add scan pending/quarantined status and administrator retry/purge actions in `ContosoDashboard/Pages/DocumentDetails.razor`
- [ ] T047 [US1] Add upload, scan pending, scan available, quarantine, quarantine retry, and purge activity/audit records in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T048 [US1] Run US1 tests and address failures until batch upload, async scanning, quarantine, retry, poison, and cleanup behavior pass

**Checkpoint**: User Story 1 is independently functional as the MVP

---

## Phase 4: User Story 2 - Find and Open an Authorized Document (Priority: P1)

**Goal**: Employees can sort, filter, search, preview, and download only documents they are authorized to access.

**Independent Test**: An authorized user lists, sorts, filters, searches, previews, and downloads an Available document; an unauthorized user receives no ID or metadata and cannot preview/download.

### Tests for User Story 2

- [ ] T049 [P] [US2] Write failing mine/shared/project/search and authorization tests in `ContosoDashboard.Tests/Services/DocumentListAndSearchTests.cs`
- [ ] T050 [P] [US2] Write failing download/preview endpoint tests for owner, project member, direct/department share, pending/quarantined, and unauthorized cases in `ContosoDashboard.Tests/Controllers/DocumentsControllerTests.cs`

### Implementation for User Story 2

- [ ] T051 [US2] Implement My Documents list, sorting by title/upload date/category/file size, and filtering by category/project/date range in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T052 [US2] Implement accessible search across title, description, tags, uploader name, and project while excluding unavailable or unauthorized documents in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T053 [US2] Implement `GET /api/documents/mine`, `/shared`, `/project/{projectId}`, and `/search` responses in `ContosoDashboard/Controllers/DocumentsController.cs`
- [ ] T054 [US2] Implement authorized `download` streaming and safe filename/original MIME disposition outside `wwwroot` in `ContosoDashboard/Controllers/DocumentsController.cs`
- [ ] T055 [US2] Implement `preview` with inline PDF/JPEG/PNG support and download/406 behavior for unsupported types in `ContosoDashboard/Controllers/DocumentsController.cs`
- [ ] T056 [US2] Record download activity and structured authorization-denial/download logs without file content in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T057 [US2] Build My Documents and search/filter/sort UI in `ContosoDashboard/Pages/Documents.razor`
- [ ] T058 [US2] Build document details with authorized preview/download actions in `ContosoDashboard/Pages/DocumentDetails.razor`
- [ ] T059 [US2] Run US2 tests and verify only authorized Available documents are listed, searched, previewed, or downloaded

**Checkpoint**: User Stories 1 and 2 are independently functional

---

## Phase 5: User Story 3 - Maintain and Share Documents (Priority: P2)

**Goal**: Owners can edit, atomically replace, permanently delete, and share documents, while recipients receive notifications and cannot maintain documents.

**Independent Test**: An owner edits/replaces/deletes/shares a document; a recipient sees Shared with Me and can access but cannot edit, replace, delete, or share.

### Tests for User Story 3

- [ ] T060 [P] [US3] Write failing metadata/update/share/delete tests for owner-only enforcement and share target integrity in `ContosoDashboard.Tests/Services/DocumentMaintenanceTests.cs`
- [ ] T061 [P] [US3] Write failing atomic replacement tests for storage failure, metadata failure, old-file retention, and old-file cleanup in `ContosoDashboard.Tests/Services/DocumentReplacementTests.cs`
- [ ] T062 [P] [US3] Write failing notification and team-share tests for direct recipients and current department changes in `ContosoDashboard.Tests/Services/DocumentSharingTests.cs`

### Implementation for User Story 3

- [ ] T063 [US3] Implement owner-only metadata update in `ContosoDashboard/Services/DocumentService.cs`, preserving DocumentId and existing metadata on failure
- [ ] T064 [US3] Implement `PUT /api/documents/{documentId}` and replacement endpoint behavior in `ContosoDashboard/Controllers/DocumentsController.cs`
- [ ] T065 [US3] Implement atomic replacement: store new GUID file first, update record/key, delete new file on database failure, and delete old file only after successful update in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T066 [US3] Implement owner-only permanent delete and coordinated metadata/storage cleanup in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T067 [US3] Implement `DELETE /api/documents/{documentId}` and confirmation UI in `ContosoDashboard/Pages/DocumentDetails.razor`
- [ ] T068 [US3] Implement direct and department share create/delete with exactly one target and owner-only authorization in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T069 [US3] Implement `POST/DELETE /api/documents/{documentId}/shares` endpoints and share controls in `ContosoDashboard/Pages/DocumentDetails.razor`
- [ ] T070 [US3] Add Share and ProjectDocument notification types and create in-app recipient notifications in `ContosoDashboard/Models/Notification.cs` and `ContosoDashboard/Services/NotificationService.cs`
- [ ] T071 [US3] Implement Shared with Me list and notification behavior in `ContosoDashboard/Pages/Documents.razor` and `ContosoDashboard/Pages/Notifications.razor`
- [ ] T072 [US3] Run US3 tests and verify maintenance, replacement atomicity, sharing, notification, and ownership behavior

**Checkpoint**: Ownership, maintenance, sharing, and notifications are independently functional

---

## Phase 6: User Story 4 - Connect Documents to Tasks and Projects (Priority: P2)

**Goal**: Active project members can associate uploads with tasks/projects and view project documents according to membership.

**Independent Test**: A project member attaches/uploads from a task and sees it in project/task lists; a non-member or role-only project manager cannot upload/view it.

### Tests for User Story 4

- [ ] T073 [P] [US4] Write failing task/project association tests for inherited projectId, active membership, role-only denial, and explicit-share exception in `ContosoDashboard.Tests/Services/DocumentProjectTaskTests.cs`
- [ ] T074 [P] [US4] Write failing project list UI-state tests for member visibility and non-member exclusion in `ContosoDashboard.Tests/Pages/DocumentProjectPageTests.cs`

### Implementation for User Story 4

- [ ] T075 [US4] Resolve task upload association from `TaskItem.ProjectId` and require active project membership in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T076 [US4] Implement project document upload and project document listing in `ContosoDashboard/Pages/ProjectDetails.razor`
- [ ] T077 [US4] Add task attachment/upload controls and task-document association in `ContosoDashboard/Pages/Tasks.razor`
- [ ] T078 [US4] Notify current project members when a new document is added to their project in `ContosoDashboard/Services/DocumentService.cs` and `ContosoDashboard/Services/NotificationService.cs`
- [ ] T079 [US4] Run US4 tests and verify project/task association and active-membership-only access

**Checkpoint**: Project/task document context is independently functional

---

## Phase 7: User Story 5 - Monitor Documents and Recent Activity (Priority: P3)

**Goal**: Users see recent document counts and administrators see audit/report data.

**Independent Test**: Dashboard displays the latest five Available uploaded documents and count for the signed-in user; administrator-only activity reports include file types, active uploaders, and access patterns.

### Tests for User Story 5

- [ ] T080 [P] [US5] Write failing dashboard and report tests for latest five, count, activity aggregates, and administrator-only report access in `ContosoDashboard.Tests/Services/DocumentDashboardReportTests.cs`

### Implementation for User Story 5

- [ ] T081 [US5] Implement latest five Available current-user uploads and document count in `ContosoDashboard/Services/DocumentService.cs` and expose `GET /api/documents/dashboard`
- [ ] T082 [US5] Update `ContosoDashboard/Pages/Index.razor` and `ContosoDashboard/Services/DashboardService.cs` to display document count and latest documents
- [ ] T083 [US5] Implement administrator report aggregation for uploaded file types, active uploaders, and access patterns in `ContosoDashboard/Services/DocumentReportService.cs`
- [ ] T084 [US5] Implement `GET /api/admin/documents/reports/activity` and an administrator report UI in `ContosoDashboard/Pages/` or a dedicated Razor page
- [ ] T085 [US5] Run US5 tests and verify dashboard and administrator reporting behavior

**Checkpoint**: All user stories are independently functional

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Security, reliability, documentation, and repository-wide validation

- [ ] T086 [P] Add or update navigation entries for Documents, Shared with Me, and administrator reports in `ContosoDashboard/Pages/_Imports.razor` and relevant layout/navigation files
- [ ] T087 [P] Review every document endpoint and service operation for owner, membership, share, Available-state, and administrator-only enforcement
- [ ] T088 [P] Add structured logging and safe exception handling to controller endpoints, queue processing, storage operations, and quarantine administration
- [ ] T089 [P] Add database migration or an intentional existing-`EnsureCreated()` recreation/update strategy and document reset behavior in `ContosoDashboard/Data/ApplicationDbContext.cs`
- [ ] T090 [P] Add cleanup validation for 30-day quarantine eligibility and administrator retry/purge operations in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T091 [P] Add queue and local/Azurite operations documentation to `specs/001-document-upload-management/quickstart.md`
- [ ] T092 [P] Add storage interface/provider migration notes and configuration examples to `specs/001-document-upload-management/quickstart.md`
- [ ] T093 [P] Run the complete build and test suite with `dotnet build` and `dotnet test` for `ContosoDashboard.sln` or the repository projects
- [ ] T094 [P] Run the quickstart upload, scan, authorization, replacement, deletion, project, dashboard, and Azurite scenarios manually and record validation results
- [ ] T095 [P] Review all generated tasks and implementation diffs for checklist format, exact file paths, and unrelated-file prevention

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies; can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion and blocks all user stories
- **User Stories (Phase 3-7)**: Depend on Foundational completion; can proceed in priority order or in parallel
- **Polish (Phase 8)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Phase 2; no dependency on other stories
- **User Story 2 (P1)**: Can start after Phase 2; uses US1 documents/storage but is independently testable
- **User Story 3 (P2)**: Can start after Phase 2; uses US1 document records and US2 authorization patterns
- **User Story 4 (P2)**: Can start after Phase 2; uses US1 upload and existing Project/Task entities
- **User Story 5 (P3)**: Can start after Phase 2; uses US1 document records and existing dashboard/service patterns

### Within Each User Story

- Tests are written first and must fail before implementation
- Models and validation precede services
- Services precede endpoints and UI integration
- Authorization is enforced in services and again in controllers/endpoints
- Story implementation completes before moving to the next priority

---

## Parallel Execution Examples

### User Story 1

```text
T030/T031/T032/T033 - Upload, replacement, queue, and function tests
T008-T013 and T016-T019 - Independent model and infrastructure files
T039/T040/T041 - Queue function implementation as a separate project
T043/T044 - API and UI integration after service contracts stabilize
```

### User Story 2

```text
T049/T050 - List/search and download/preview tests
T051/T052 - Independent list/search implementation
T054/T055 - Independent download/preview endpoint implementation
T057/T058 - Independent My Documents and details UI work
```

### User Story 3

```text
T060/T061/T062 - Maintenance, replacement, and sharing tests
T063/T065/T066 - Service maintenance operations after test contracts are established
T064/T067/T069 - Separate API and UI endpoints
T070/T071 - Notification and Shared with Me integration
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational
3. Complete Phase 3: User Story 1
4. Validate independent upload, queue, pending, quarantine, retry, poison, and cleanup behavior
5. Stop and demo the MVP before other stories

### Incremental Delivery

1. Setup + Foundational
2. Add User Story 1: secure upload and async scan
3. Add User Story 2: authorized discovery and preview/download
4. Add User Story 3: maintenance and sharing
5. Add User Story 4: project/task integration
6. Add User Story 5: dashboard and administrator reports
7. Finish with Polish and cross-cutting validation

### Parallel Team Strategy

1. Team completes Setup and Foundational together
2. Developer A owns US1 and Queue Storage/Azure Functions
3. Developer B owns US2 list/search/download/preview
4. Developer C owns US3 maintenance/sharing
5. Developer D owns US4 and US5 integration

---

## Notes

- [P] tasks affect different files or independent work units
- [Story] labels identify all user-story-phase tasks
- Setup and Foundational tasks intentionally have no [Story] labels
- The Azure Functions Queue Storage trigger and Queue Storage retries/poison handling are implementation requirements, not optional extensions
- Preserve existing mock authentication and do not introduce secrets or production credentials into source control
- Avoid: vague tasks, missing file paths, all-or-nothing batch uploads, role-only project authority, and exposing pending/quarantined files
