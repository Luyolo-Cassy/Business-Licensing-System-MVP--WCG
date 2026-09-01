namespace BusinessLicensing_Practice.Services
{
    public class ApplicationFileService
    {
        private readonly string storageRoot;

        public ApplicationFileService(IWebHostEnvironment environment)
        {
            storageRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "generated-applications"));
        }

        public async Task<string> SaveGeneratedPdfAsync(int applicationId, string fileName, byte[] contents)
        {
            var safeFileName = Path.GetFileName(fileName);
            var relativePath = Path.Combine(applicationId.ToString(), safeFileName);
            var fullPath = ResolvePath(relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, contents);

            return relativePath.Replace('\\', '/');
        }

        public string? GetGeneratedPdfPath(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath.StartsWith('/'))
            {
                return null;
            }

            var fullPath = ResolvePath(relativePath);
            return File.Exists(fullPath) ? fullPath : null;
        }

        private string ResolvePath(string relativePath)
        {
            var fullPath = Path.GetFullPath(Path.Combine(storageRoot, relativePath));
            var rootPrefix = storageRoot.EndsWith(Path.DirectorySeparatorChar)
                ? storageRoot
                : storageRoot + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The generated file path is outside the application storage directory.");
            }

            return fullPath;
        }
    }
}
