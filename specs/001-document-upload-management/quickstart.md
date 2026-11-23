# Quickstart: Document Upload and Management

## Prerequisites

- Windows with SQL Server LocalDB
- Repository SDK .NET 10
- The existing ContosoDashboard mock login
- A local `AppData/uploads` directory available to the application process
- Optional for local queue processing: Azurite and Azure Functions Core Tools

## Setup

From the repository root:

```powershell
dotnet restore .\ContosoDashboard.slnx
dotnet build .\ContosoDashboard.slnx
dotnet run --project .\ContosoDashboard\ContosoDashboard.csproj
```

Sign in through the existing mock login and select a seeded user.

By default, Development uses the local pending-scan worker and local test scanner, so uploads remain local and do not require Azure services. This scanner is a training/test implementation only and is not suitable for production malware protection.

To run the Azure Queue Storage trigger locally:

```powershell
azurite --queue
Copy-Item .\DocumentScanner.Function\local.settings.json.example .\DocumentScanner.Function\local.settings.json
func start --project .\DocumentScanner.Function
```

For queue-driven scanning, set `DocumentUpload:UseLocalScanWorker` to `false` in the web application's development configuration and use `AzureWebJobsStorage=UseDevelopmentStorage=true`. The Function project points its local file provider at `..\ContosoDashboard\AppData\uploads`; keep both processes on the same local filesystem. Azurite uses the queue `document-scans`, with bounded retries routed to `document-scans-poison`. No file bytes or secrets belong in queue messages. In Azure, configure `DocumentQueueServiceUri` and grant the app identity the required Queue Storage role rather than storing a connection key.

The application stores local files in `ContosoDashboard/AppData/uploads`; do not place the upload directory under `wwwroot`. Development database initialization uses `EnsureCreated()`. To recreate development-only document metadata, stop the application and recreate/reset the LocalDB database, then restart so seed and document tables are created again.

Queue host settings use `document-scans` as the trigger queue and `document-scans-poison` for repeated infrastructure failures. Copy `DocumentScanner.Function/local.settings.json.example` to `local.settings.json` for local-only Azurite placeholders; do not add credentials or connection secrets to source control. The local settings file is ignored by git. Cloud deployments should use managed identity or a secret provider and a production malware scanner implementation.

## Validation status

- `dotnet build ContosoDashboard.slnx`: passed.
- `dotnet test ContosoDashboard.slnx`: passed (40 dashboard/service tests and 5 scan-worker tests).
- Manual browser/LocalDB/Azurite walkthrough: not run in the current environment because `sqllocaldb`, `azurite`, and Azure Functions Core Tools (`func`) are unavailable. Complete the scenarios below after installing those prerequisites.

## Validation Scenarios

### 1. Batch upload and partial success

1. Select a PDF under 25 MB, an Office file under 25 MB, and an unsupported file.
2. Provide title and category.
3. Submit.
4. Confirm each file has an independent result.
5. Confirm available successful files remain and unsupported or failed files can be retried.
6. Retry only failed files and confirm successful files are not uploaded again.

Expected: successful files become visible, failures identify size/type/scan/storage reason, and no failed/unknown file is exposed.

### 2. My Documents and authorization

1. Upload a personal document.
2. Open My Documents.
3. Sort by title/date/category/size and filter by category/project/date.
4. Sign in as another user.

Expected: My Documents contains only the uploader's documents for the first user. Another user cannot access the personal document unless separately shared.

### 3. Malware quarantine

1. Use the testable scanner boundary to return a confirmed scan failure.
2. Submit a supported file.

Expected: file is quarantine state, inaccessible by ordinary users, remains for the quarantine period, and scan details are available only to authorized administrators.

1. Use scanner unavailable behavior.
2. Submit another supported file.

Expected: document remains Pending and inaccessible until successful scan.

### 4. Project membership

1. Sign in as an active member of project 1.
2. Upload a document from project view or associated task.
3. Confirm project association is inherited and document is visible to project members.
4. Sign in as a team lead or project manager who is not a project member.

Expected: active member succeeds. Role-only team lead/project manager cannot upload or view the project document.

### 5. Replacement atomicity

1. As owner, replace a document file.
2. Confirm Document ID and metadata remain unchanged.
3. Simulate a replacement storage failure.

Expected: old file remains available and old metadata/storage key is unchanged.

### 6. Ownership and sharing

1. As owner, edit metadata and share with a named user and a department.
2. Sign in as a direct recipient and verify Shared with Me.
3. Sign in as a department member and verify access changes after department membership/current department changes.
4. Attempt edit/delete by the recipient.

Expected: recipients can view/download according to grant but cannot edit/delete/share. Leaving the department or losing membership removes access immediately.

### 7. Delete and storage cleanup

1. As owner, confirm deletion.
2. Verify the document is absent from My Documents, project lists, search, and shared lists.
3. Verify storage bytes are removed.

Expected: permanent metadata and storage deletion; project managers without ownership cannot delete.

### 8. Dashboard and audit

1. Upload several documents for the signed-in user.
2. Open dashboard.

Expected: latest five uploaded/available documents and current document count appear.

3. Download/share/edit/delete documents as authorized users.
4. Open administrator activity report.

Expected: report includes file type, active uploader, access patterns, and document activity without file content.
