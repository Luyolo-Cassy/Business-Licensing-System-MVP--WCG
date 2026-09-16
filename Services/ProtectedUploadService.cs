using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessLicensing_Practice.Services;

public class ProtectedUploadService(IWebHostEnvironment environment)
{
    private readonly string root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "protected-uploads"));
    private readonly string legacyRoot = Path.GetFullPath(Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "uploads"));

    public async Task<string> SaveAsync(string originalName, byte[] bytes)
    {
        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        if (extension is not (".pdf" or ".png" or ".jpg" or ".jpeg")) throw new InvalidOperationException("Unsupported document type.");
        Directory.CreateDirectory(root);
        var name = Guid.NewGuid().ToString("N") + extension;
        await File.WriteAllBytesAsync(Path.Combine(root, name), bytes);
        // This is an authorized compatibility URL, not a public static path.
        return "/uploads/" + name;
    }

    public int MoveLegacyUploads()
    {
        if (!Directory.Exists(legacyRoot)) return 0;
        if ((File.GetAttributes(legacyRoot) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The legacy upload directory must not be a link.");
        Directory.CreateDirectory(root);
        // Validate all targets before moving anything. Never overwrite or delete an existing file.
        var moves = Directory.GetFiles(legacyRoot, "*", SearchOption.AllDirectories)
            .Select(source => (Source: Path.GetFullPath(source), Target: Path.GetFullPath(Path.Combine(root, Path.GetRelativePath(legacyRoot, source)))))
            .ToList();
        foreach (var directory in Directory.GetDirectories(legacyRoot, "*", SearchOption.AllDirectories))
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Legacy upload subdirectories must not be links.");
        foreach (var move in moves)
        {
            if (!move.Source.StartsWith(legacyRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !move.Target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                (File.GetAttributes(move.Source) & FileAttributes.ReparsePoint) != 0 || File.Exists(move.Target))
                throw new InvalidOperationException("Legacy upload migration found an unsafe path or an existing destination. No files will be overwritten.");
        }
        foreach (var move in moves)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(move.Target)!);
            File.Move(move.Source, move.Target, overwrite: false);
        }
        return moves.Count;
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
        if (candidates.Count == 0) return Results.NotFound();
        foreach (var application in candidates)
        {
            if (!await ApplicationReadAccess.CanReadAsync(db, users, context.User, application)) continue;
            var fullPath = Path.GetFullPath(Path.Combine(root, name));
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath)) return Results.NotFound();
            // Download rather than execute arbitrary historical file formats in the application origin.
            return Results.File(fullPath, "application/octet-stream", name);
        }
        return Results.Forbid();
    }
}
