using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using BusinessLicensing_Practice.Services.Storage;

namespace BusinessLicensing_Practice.Services;

public class ProtectedUploadService(IPrivateFileStore store, IWebHostEnvironment environment)
{
    private readonly string legacyRoot = Path.GetFullPath(Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "uploads"));

    public async Task<string> SaveAsync(string originalName, byte[] bytes)
    {
        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        if (extension is not (".pdf" or ".png" or ".jpg" or ".jpeg")) throw new InvalidOperationException("Unsupported document type.");
        var name = Guid.NewGuid().ToString("N") + extension;
        var contentType = extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
        var objectKey = PrivateFileKeys.ProtectedUpload(name);
        await store.SaveAsync(objectKey, bytes, contentType);
        // This is an authorized compatibility URL, not a public static path.
        return PrivateFileKeys.ToUploadReference(objectKey);
    }

    public async Task DeleteIfUnreferencedAsync(ApplicationDbContext db, string reference)
    {
        if (!TryResolveReference(reference, out var objectKey)) return;
        var referenced = await db.ApplicationDocuments.AsNoTracking().AnyAsync(item => item.FilePath == reference) ||
            await db.ApplicationDraftDocuments.AsNoTracking().AnyAsync(item => item.FilePath == reference);
        if (!referenced) await store.DeleteAsync(objectKey);
    }

    public int MoveLegacyUploads()
    {
        if (store is not LocalPrivateFileStore localStore)
            throw new InvalidOperationException("Legacy upload migration is available only with local private-file storage.");
        return localStore.MoveLegacyUploads(legacyRoot);
    }

    public async Task<IResult> DownloadAsync(HttpContext context, ApplicationDbContext db, UserManager<ApplicationUser> users)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (context.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)) return Results.StatusCode(405);
        var path = context.Request.Path.Value ?? "";
        if (!path.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)) return Results.NotFound();
        var name = path["/uploads/".Length..];
        // Uploaded filenames are flat, server-generated keys. Reject alternate path representations.
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(['/', '\\', ':', '\0', '%']) >= 0 ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return Results.NotFound();
        var reference = "/uploads/" + name;
        var candidates = await db.Applications.AsNoTracking().Where(a => a.Documents.Any(d => d.FilePath == reference) ||
            a.ApplicationFormFilePath == reference || a.UploadedDocumentPath == reference).ToListAsync();
        foreach (var application in candidates)
        {
            if (!await ApplicationReadAccess.CanReadAsync(db, users, context.User, application)) continue;
            if (!TryResolveReference(reference, out var objectKey)) return Results.NotFound();
            var file = await store.OpenReadAsync(objectKey, context.RequestAborted);
            if (file == null) return Results.NotFound();
            // Download rather than execute arbitrary historical file formats in the application origin.
            return Results.Stream(file.Content, "application/octet-stream", name);
        }
        var user = await users.GetUserAsync(context.User);
        var stamp = context.User.FindFirstValue("AspNet.Identity.SecurityStamp");
        var ownsDraftDocument = user != null && await users.IsInRoleAsync(user, "BusinessOwner") && !await users.IsLockedOutAsync(user) &&
            (stamp == null || stamp == user.SecurityStamp) &&
            await db.ApplicationDraftDocuments.AsNoTracking()
                .AnyAsync(item => item.FilePath == reference && item.ApplicationDraft!.UserId == user.Id);
        if (ownsDraftDocument)
        {
            if (!TryResolveReference(reference, out var objectKey)) return Results.NotFound();
            var file = await store.OpenReadAsync(objectKey, context.RequestAborted);
            if (file == null) return Results.NotFound();
            return Results.Stream(file.Content, "application/octet-stream", name);
        }
        return candidates.Count == 0 ? Results.NotFound() : Results.Forbid();
    }

    private static bool TryResolveReference(string reference, out string objectKey)
    {
        objectKey = "";
        if (!reference.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)) return false;
        var name = reference["/uploads/".Length..];
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', ':', '\0', '%']) >= 0 ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        try
        {
            objectKey = PrivateFileKeys.ProtectedUpload(name);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
