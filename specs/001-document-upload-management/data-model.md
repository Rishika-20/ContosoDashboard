# Data Model: Document Upload and Management

## Document

Represents one uploaded document and its current available, pending, or quarantined state.

| Field | Type | Rules |
|---|---|---|
| DocumentId | int, PK | Preserved across metadata edits and replacements |
| Title | string(255) | Required, trimmed |
| Description | string(2000), nullable | Optional |
| Category | string(100) | Required predefined value |
| UploaderId | int, FK User | Required; immutable owner/uploader |
| ProjectId | int?, FK Project | Optional; task uploads inherit task project |
| OriginalFileName | string(255) | User-provided display name only; never used for storage |
| FileSizeBytes | long | 1..26214400 |
| OriginalFileType | string(255) | MIME type captured at upload |
| SupportedFileType | string(32) | PDF, DOCX, XLSX, PPTX, TXT, JPEG, PNG |
| StorageKey | string(500) | Relative GUID-based key; no absolute path or unsafe name |
| UploadDateUtc | datetime | Immutable upload time |
| UpdatedDateUtc | datetime | Metadata/replacement activity time |
| ScanStatus | enum | Pending, Available, Quarantined |
| QuarantineFailureDetails | string(2000), nullable | Security detail for authorized administrators |
| QuarantinedUntilUtc | datetime?, nullable | Expected 30 days after confirmed failure |
| TaskId | int?, FK TaskItem | Optional direct task association |

Relationships:

- `Document.Uploader` to `User`
- Optional `Document.Project`
- Optional `Document.Task`
- `Document.Tags`
- `Document.Shares`
- `Document.Activities`

Indexes: uploader + upload date; project + availability; category; title/full metadata search fields.

## DocumentShare

Represents an explicit user or department/team grant.

| Field | Type | Rules |
|---|---|---|
| DocumentShareId | int, PK | |
| DocumentId | int, FK | Required |
| TargetUserId | int?, FK User | Set for direct user share; mutually exclusive with department |
| TargetDepartment | string(100), nullable | Set for department/team share; mutually exclusive with user |
| GrantedByUserId | int, FK User | Must be the document owner at grant time |
| GrantedAtUtc | datetime | |

Authorization recalculates department shares from current `User.Department`; direct shares remain until revoked. Deleting a document removes its shares.

## DocumentScanAttempt

Tracks asynchronous processing and retries for an upload.

| Field | Type | Rules |
|---|---|---|
| DocumentScanAttemptId | long, PK | |
| DocumentId | int, FK | Required |
| CorrelationId | GUID | Unique for a queue job |
| StorageKey | string(500) | Snapshot of the file location processed |
| Status | enum | Pending, InProgress, Available, Quarantined, TransientFailure, Poisoned |
| AttemptNumber | int | Starting at 1 |
| EnqueuedAtUtc | datetime | |
| CompletedAtUtc | datetime?, nullable | |
| FailureDetails | string(2000), nullable | Sanitized operator-facing details; no secrets or file content |

The Azure Functions queue message contains only `DocumentId`, `StorageKey`, `Attempt`, and `CorrelationId`. Queue processing is idempotent for the document/correlation pair.

## DocumentTag

| Field | Type | Rules |
|---|---|---|
| DocumentTagId | int, PK | |
| DocumentId | int, FK | |
| Name | string(100) | Trimmed, unique per document, case-normalized |

## DocumentActivity

Append-only audit activity.

| Field | Type | Rules |
|---|---|---|
| DocumentActivityId | int, PK | |
| DocumentId | int?, FK | Nullable only if a pre-recorded document operation cleans up metadata |
| ActorUserId | int, FK User | Required |
| ActivityType | enum | Upload, Download, Delete, Share, MetadataChange, Replace, ScanPending, ScanAvailable, Quarantine, QuarantineRetry, Purge |
| EventTimeUtc | datetime | |
| ProjectId | int?, FK | Context snapshot |
| TargetUserId | int?, FK | Share/recipient context |
| TargetDepartment | string(100), nullable | Team context |
| Outcome | enum/string | Success or safe failure reason |
| Details | string(1000), nullable | No file content, token, password, or sensitive payload |

Indexes: document + event time; actor + event time; project + event time; activity type + event time.

## Existing Entity Use

- `User.Department`, `Role`, `UserId`, and `DisplayName` support identity and department shares.
- `ProjectMember` is the source of current project membership. No role is inferred from it.
- `TaskItem.ProjectId` supplies the project for task attachments.
- `Notification` is reused for document share and project-document addition notifications.

## State Transitions

```text
Upload validation success
  -> Storage saved
  -> Pending
     -> Available (successful malware scan)
     -> Quarantined (confirmed failure)

Quarantined
  -> Available (administrator-authorized successful retry)
  -> permanently removed (administrator-authorized purge)
  -> permanently removed after 30 days? (No automatic purge authority is assumed; quarantine record expires and purge remains administrator action)

Pending
  -> InProgress -> Available (successful queue scan)
  -> InProgress -> Quarantined (confirmed malware result)
  -> InProgress -> Pending (transient scanner/infrastructure failure; Queue Storage retry)
  -> Poisoned (bounded dequeue failures; remains inaccessible pending administrator retry)

Available
  -> Available (metadata update or successful replacement)
  -> deleted (owner permanent delete)
```

The 30-day quarantine requirement means files remain inaccessible and are eligible for administrator retry/purge during that period. Automatic purge timing is not inferred by this plan and should be implemented only if explicitly scheduled by operational policy.

## Validation and Integrity Rules

- One of `TargetUserId` or `TargetDepartment` must be set on `DocumentShare`.
- Category must be from the predefined category list.
- Original file extension and MIME type must both belong to an allowlisted pairing.
- File size is at most 25 MB and must match the stored bytes.
- Storage keys must be GUID-based and never derive from user-supplied names.
- A project document is associated only with an existing project.
- Task document association automatically uses the task's project.
- Deletion and metadata operations require current owner identity.
