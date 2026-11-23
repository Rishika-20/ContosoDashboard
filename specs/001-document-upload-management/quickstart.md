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
dotnet restore .\ContosoDashboard\ContosoDashboard.csproj
dotnet build .\ContosoDashboard\ContosoDashboard.csproj
dotnet run --project .\ContosoDashboard\ContosoDashboard.csproj
```

Sign in through the existing mock login and select a seeded user.

To run the asynchronous scanner locally:

```powershell
azurite --queue
func start --project .\DocumentScanner.Function
```

Configure `AzureWebJobsStorage` for Azurite and set the web application's queue connection to `document-scans`. No file bytes or secrets belong in queue messages.

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
