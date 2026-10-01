namespace BusinessLicensing_Practice.Services.Storage;

public sealed class LocalPrivateFileStore(IWebHostEnvironment environment) : IPrivateFileStore
{
    private readonly string root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data"));

    public async Task SaveAsync(string objectKey, ReadOnlyMemory<byte> contents, string contentType,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await stream.WriteAsync(contents, cancellationToken);
    }

    public Task<PrivateFile?> OpenReadAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = ResolvePath(objectKey);
        if (!File.Exists(fullPath)) return Task.FromResult<PrivateFile?>(null);

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult<PrivateFile?>(new PrivateFile(stream, null, stream.Length));
    }

    public async Task<byte[]?> ReadAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        var file = await OpenReadAsync(objectKey, cancellationToken);
        if (file == null) return null;
        await using var stream = file.Content;
        using var buffer = file.Length is > 0 and <= int.MaxValue
            ? new MemoryStream((int)file.Length.Value)
            : new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    public Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(ResolvePath(objectKey)));
    }

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = ResolvePath(objectKey);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public int MoveLegacyUploads(string legacyRoot)
    {
        legacyRoot = Path.GetFullPath(legacyRoot);
        if (!Directory.Exists(legacyRoot)) return 0;
        if ((File.GetAttributes(legacyRoot) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("The legacy upload directory must not be a link.");

        var protectedRoot = ResolvePath(PrivateFileKeys.ProtectedUploadsPrefix);
        Directory.CreateDirectory(protectedRoot);
        var moves = Directory.GetFiles(legacyRoot, "*", SearchOption.AllDirectories)
            .Select(source => (Source: Path.GetFullPath(source),
                Target: Path.GetFullPath(Path.Combine(protectedRoot, Path.GetRelativePath(legacyRoot, source)))))
            .ToList();
        foreach (var directory in Directory.GetDirectories(legacyRoot, "*", SearchOption.AllDirectories))
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Legacy upload subdirectories must not be links.");
        foreach (var move in moves)
        {
            if (!IsWithin(move.Source, legacyRoot) || !IsWithin(move.Target, protectedRoot) ||
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

    private string ResolvePath(string objectKey)
    {
        var normalized = PrivateFileKeys.Normalize(objectKey);
        var relativePath = normalized.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!IsWithin(fullPath, root))
            throw new InvalidOperationException("The private-file object key is outside the storage root.");
        return fullPath;
    }

    private static bool IsWithin(string path, string directory)
    {
        var prefix = directory.EndsWith(Path.DirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.StartsWith(prefix, comparison);
    }
}
