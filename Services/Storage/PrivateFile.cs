namespace BusinessLicensing_Practice.Services.Storage;

public sealed record PrivateFile(Stream Content, string? ContentType, long? Length);
