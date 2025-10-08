using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportRental.MediaStorage.Data;
using SportRental.MediaStorage.Models;
using SportRental.MediaStorage.Options;
using SportRental.MediaStorage.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=media-storage.db";
builder.Services.AddDbContext<MediaStorageDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddScoped<FileStorageService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MediaStorageDbContext>();
    await db.Database.EnsureCreatedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

static bool IsAuthorized(HttpRequest request, SecurityOptions options)
{
    if (options.ApiKeys.Length == 0)
    {
        return true;
    }

    if (!request.Headers.TryGetValue("X-Api-Key", out var values))
    {
        return false;
    }

    var provided = values.FirstOrDefault();
    return provided is not null && options.ApiKeys.Contains(provided, StringComparer.Ordinal);
}

app.MapPost("/api/files", async (
    HttpContext context,
    FileStorageService storage,
    IOptions<SecurityOptions> securityOptions,
    CancellationToken cancellationToken) =>
{
    if (!IsAuthorized(context.Request, securityOptions.Value))
    {
        return Results.Unauthorized();
    }

    if (!context.Request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart/form-data payload" });
    }

    var form = await context.Request.ReadFormAsync(cancellationToken);
    if (!Guid.TryParse(form["tenantId"].FirstOrDefault(), out var tenantId))
    {
        return Results.BadRequest(new { error = "Missing or invalid tenantId" });
    }

    var file = form.Files.FirstOrDefault();
    if (file is null)
    {
        return Results.BadRequest(new { error = "File field is required" });
    }

    try
    {
        var requestedPath = form["path"].FirstOrDefault();
        var stored = await storage.SaveAsync(tenantId, file, requestedPath, cancellationToken);
        var downloadUrl = storage.BuildPublicUrl(stored, context.Request);
        return Results.Created($"/api/files/{stored.Id}", new
        {
            stored.Id,
            stored.TenantId,
            stored.OriginalFileName,
            stored.ContentType,
            stored.Size,
            stored.CreatedAtUtc,
            stored.Sha256,
            downloadUrl,
            stored.RelativePath
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("UploadFile")
.WithTags("Files")
.DisableAntiforgery();

app.MapGet("/api/files/{id:guid}", async (
    Guid id,
    FileStorageService storage,
    HttpRequest request,
    CancellationToken cancellationToken) =>
{
    var file = await storage.FindAsync(id, cancellationToken);
    if (file is null)
    {
        return Results.NotFound();
    }

    var downloadUrl = storage.BuildPublicUrl(file, request);
    return Results.Ok(new
    {
        file.Id,
        file.TenantId,
        file.OriginalFileName,
        file.ContentType,
        file.Size,
        file.CreatedAtUtc,
        file.Sha256,
        DownloadUrl = downloadUrl
    });
})
.WithName("GetFileMetadata")
.WithTags("Files");

app.MapDelete("/api/files/{id:guid}", async (
    Guid id,
    FileStorageService storage,
    IOptions<SecurityOptions> securityOptions,
    HttpRequest request,
    CancellationToken cancellationToken) =>
{
    if (!IsAuthorized(request, securityOptions.Value))
    {
        return Results.Unauthorized();
    }

    var removed = await storage.DeleteAsync(id, cancellationToken);
    return removed ? Results.NoContent() : Results.NotFound();
})
.WithName("DeleteFile")
.WithTags("Files");

app.MapGet("/files/{**relativePath}", (string relativePath, FileStorageService storage) =>
{
    try
    {
        var normalized = FileStorageService.NormalizeRelativePath(relativePath);
        var absolutePath = storage.GetAbsolutePath(normalized);
        if (!File.Exists(absolutePath))
        {
            return Results.NotFound();
        }

        var provider = new FileExtensionContentTypeProvider();
        var fileName = Path.GetFileName(absolutePath);
        if (!provider.TryGetContentType(fileName, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        var stream = File.OpenRead(absolutePath);
        return Results.File(stream, contentType, enableRangeProcessing: true);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("DownloadFile")
.WithTags("Files");

app.MapMethods("/files/{**relativePath}", new[] { HttpMethods.Head }, (string relativePath, FileStorageService storage) =>
{
    try
    {
        var normalized = FileStorageService.NormalizeRelativePath(relativePath);
        var absolutePath = storage.GetAbsolutePath(normalized);
        return File.Exists(absolutePath) ? Results.Ok() : Results.NotFound();
    }
    catch
    {
        return Results.BadRequest();
    }
})
.WithTags("Files");

app.Run();

public partial class Program;

