using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Data;
using ContosoDashboard.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddControllers();

// Add authentication state provider for Blazor
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthenticationStateProvider>();

// Configure Database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure Mock Authentication (Cookie-based for training purposes)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

// Add authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Employee", policy => policy.RequireRole("Employee", "TeamLead", "ProjectManager", "Administrator"));
    options.AddPolicy("TeamLead", policy => policy.RequireRole("TeamLead", "ProjectManager", "Administrator"));
    options.AddPolicy("ProjectManager", policy => policy.RequireRole("ProjectManager", "Administrator"));
    options.AddPolicy("Administrator", policy => policy.RequireRole("Administrator"));
});

// Register application services
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
builder.Services.AddScoped<IMalwareScanner, TestableMalwareScanner>();
builder.Services.AddScoped<IDocumentQueueService, DocumentQueueService>();
builder.Services.AddScoped<DocumentAuthorizationService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<DocumentScanProcessor>();
builder.Services.AddScoped<DocumentReportService>();
builder.Services.AddHostedService<LocalDocumentScanWorker>();

// Add HttpContextAccessor for accessing user claims
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex) when (context.Request.Path.StartsWithSegments("/api/documents") ||
                               context.Request.Path.StartsWithSegments("/api/admin/documents"))
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Unhandled document API exception for path {RequestPath}", context.Request.Path);
        if (context.Response.HasStarted) throw;
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { status = 500, title = "Document operation failed" });
    }
});

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.EnsureCreated(); // For development - use migrations in production
        EnsureDocumentSchema(context);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred creating the database.");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    // Use HSTS even in development for training purposes
    app.UseHsts();
}

// Add security headers
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    
    // Content Security Policy for Blazor Server
    context.Response.Headers["Content-Security-Policy"] = 
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
        "font-src 'self' https://cdn.jsdelivr.net; " +
        "img-src 'self' data: https:; " +
        "connect-src 'self' wss: ws:;";
    
    await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Enable authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapBlazorHub();
app.MapControllers();
app.MapFallbackToPage("/_Host");

app.Run();

static void EnsureDocumentSchema(ApplicationDbContext context)
{
    context.Database.ExecuteSqlRaw("""
        IF OBJECT_ID(N'[Documents]', N'U') IS NULL
        BEGIN
            CREATE TABLE [Documents] (
                [DocumentId] int IDENTITY(1,1) NOT NULL,
                [Title] nvarchar(255) NOT NULL,
                [Description] nvarchar(2000) NULL,
                [Category] nvarchar(100) NOT NULL CONSTRAINT [DF_Documents_Category] DEFAULT N'General',
                [UploaderId] int NOT NULL,
                [ProjectId] int NULL,
                [TaskId] int NULL,
                [OriginalFileName] nvarchar(255) NOT NULL,
                [FileSizeBytes] bigint NOT NULL,
                [OriginalFileType] nvarchar(255) NOT NULL,
                [SupportedFileType] nvarchar(32) NOT NULL,
                [StorageKey] nvarchar(500) NOT NULL,
                [UploadDateUtc] datetime2 NOT NULL,
                [UpdatedDateUtc] datetime2 NOT NULL,
                [ScanStatus] int NOT NULL CONSTRAINT [DF_Documents_ScanStatus] DEFAULT 0,
                [QuarantineFailureDetails] nvarchar(2000) NULL,
                [QuarantinedUntilUtc] datetime2 NULL,
                CONSTRAINT [PK_Documents] PRIMARY KEY ([DocumentId]),
                CONSTRAINT [FK_Documents_Users_UploaderId] FOREIGN KEY ([UploaderId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION,
                CONSTRAINT [FK_Documents_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE SET NULL,
                CONSTRAINT [FK_Documents_Tasks_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [Tasks] ([TaskId]) ON DELETE SET NULL
            );
            CREATE INDEX [IX_Documents_UploaderId_UploadDateUtc] ON [Documents] ([UploaderId], [UploadDateUtc]);
            CREATE INDEX [IX_Documents_ProjectId_ScanStatus] ON [Documents] ([ProjectId], [ScanStatus]);
            CREATE INDEX [IX_Documents_Category] ON [Documents] ([Category]);
            CREATE INDEX [IX_Documents_Title] ON [Documents] ([Title]);
        END;

        IF OBJECT_ID(N'[DocumentShares]', N'U') IS NULL
        BEGIN
            CREATE TABLE [DocumentShares] (
                [DocumentShareId] int IDENTITY(1,1) NOT NULL,
                [DocumentId] int NOT NULL,
                [TargetUserId] int NULL,
                [TargetDepartment] nvarchar(100) NULL,
                [GrantedByUserId] int NOT NULL,
                [GrantedAtUtc] datetime2 NOT NULL,
                CONSTRAINT [PK_DocumentShares] PRIMARY KEY ([DocumentShareId]),
                CONSTRAINT [FK_DocumentShares_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([DocumentId]) ON DELETE CASCADE,
                CONSTRAINT [FK_DocumentShares_Users_TargetUserId] FOREIGN KEY ([TargetUserId]) REFERENCES [Users] ([UserId]) ON DELETE SET NULL,
                CONSTRAINT [FK_DocumentShares_Users_GrantedByUserId] FOREIGN KEY ([GrantedByUserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION
            );
            CREATE INDEX [IX_DocumentShares_TargetUserId] ON [DocumentShares] ([TargetUserId]);
            CREATE INDEX [IX_DocumentShares_TargetDepartment] ON [DocumentShares] ([TargetDepartment]);
        END;

        IF OBJECT_ID(N'[DocumentTags]', N'U') IS NULL
        BEGIN
            CREATE TABLE [DocumentTags] (
                [DocumentTagId] int IDENTITY(1,1) NOT NULL,
                [DocumentId] int NOT NULL,
                [Name] nvarchar(100) NOT NULL,
                CONSTRAINT [PK_DocumentTags] PRIMARY KEY ([DocumentTagId]),
                CONSTRAINT [FK_DocumentTags_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([DocumentId]) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX [IX_DocumentTags_DocumentId_Name] ON [DocumentTags] ([DocumentId], [Name]);
        END;

        IF OBJECT_ID(N'[DocumentScanAttempts]', N'U') IS NULL
        BEGIN
            CREATE TABLE [DocumentScanAttempts] (
                [DocumentScanAttemptId] bigint IDENTITY(1,1) NOT NULL,
                [DocumentId] int NOT NULL,
                [CorrelationId] uniqueidentifier NOT NULL,
                [StorageKey] nvarchar(500) NOT NULL,
                [Status] int NOT NULL,
                [AttemptNumber] int NOT NULL,
                [EnqueuedAtUtc] datetime2 NOT NULL,
                [CompletedAtUtc] datetime2 NULL,
                [FailureDetails] nvarchar(2000) NULL,
                CONSTRAINT [PK_DocumentScanAttempts] PRIMARY KEY ([DocumentScanAttemptId]),
                CONSTRAINT [FK_DocumentScanAttempts_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([DocumentId]) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX [IX_DocumentScanAttempts_DocumentId_CorrelationId] ON [DocumentScanAttempts] ([DocumentId], [CorrelationId]);
            CREATE INDEX [IX_DocumentScanAttempts_Status_EnqueuedAtUtc] ON [DocumentScanAttempts] ([Status], [EnqueuedAtUtc]);
        END;

        IF OBJECT_ID(N'[DocumentActivities]', N'U') IS NULL
        BEGIN
            CREATE TABLE [DocumentActivities] (
                [DocumentActivityId] int IDENTITY(1,1) NOT NULL,
                [DocumentId] int NULL,
                [ActorUserId] int NOT NULL,
                [ActivityType] int NOT NULL,
                [EventTimeUtc] datetime2 NOT NULL,
                [ProjectId] int NULL,
                [TargetUserId] int NULL,
                [TargetDepartment] nvarchar(100) NULL,
                [Outcome] int NOT NULL,
                [Details] nvarchar(1000) NULL,
                CONSTRAINT [PK_DocumentActivities] PRIMARY KEY ([DocumentActivityId]),
                CONSTRAINT [FK_DocumentActivities_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([DocumentId]) ON DELETE SET NULL,
                CONSTRAINT [FK_DocumentActivities_Users_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [Users] ([UserId]) ON DELETE NO ACTION,
                CONSTRAINT [FK_DocumentActivities_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE SET NULL,
                CONSTRAINT [FK_DocumentActivities_Users_TargetUserId] FOREIGN KEY ([TargetUserId]) REFERENCES [Users] ([UserId]) ON DELETE SET NULL
            );
            CREATE INDEX [IX_DocumentActivities_DocumentId_EventTimeUtc] ON [DocumentActivities] ([DocumentId], [EventTimeUtc]);
            CREATE INDEX [IX_DocumentActivities_ActorUserId_EventTimeUtc] ON [DocumentActivities] ([ActorUserId], [EventTimeUtc]);
            CREATE INDEX [IX_DocumentActivities_ProjectId_EventTimeUtc] ON [DocumentActivities] ([ProjectId], [EventTimeUtc]);
            CREATE INDEX [IX_DocumentActivities_ActivityType_EventTimeUtc] ON [DocumentActivities] ([ActivityType], [EventTimeUtc]);
        END;
        """);
}
