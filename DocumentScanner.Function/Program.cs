using ContosoDashboard.Data;
using ContosoDashboard.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(context.Configuration.GetConnectionString("DefaultConnection")));
        services.AddSingleton<IFileStorageService>(sp =>
            new LocalFileStorageService(context.Configuration,
                new FunctionWebHostEnvironment(context.HostingEnvironment.ContentRootPath),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LocalFileStorageService>>()));
        services.AddScoped<IMalwareScanner, TestableMalwareScanner>();
        services.AddScoped<DocumentScanProcessor>();
    })
    .Build();

await host.RunAsync();

public sealed class FunctionWebHostEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
{
    public FunctionWebHostEnvironment(string contentRootPath) { ContentRootPath = contentRootPath; WebRootPath = contentRootPath; }
    public string ApplicationName { get; set; } = "DocumentScanner.Function";
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    public string WebRootPath { get; set; }
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    public string ContentRootPath { get; set; }
    public string EnvironmentName { get; set; } = "Development";
}
