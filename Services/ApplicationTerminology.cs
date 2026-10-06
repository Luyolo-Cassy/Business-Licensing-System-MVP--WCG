namespace BusinessLicensing_Practice.Services;

public static class ApplicationTerminology
{
    public static string ForDisplay(string? value) => (value ?? "")
        .Replace("Licences", "Licenses", StringComparison.Ordinal)
        .Replace("licences", "licenses", StringComparison.Ordinal)
        .Replace("Licence", "License", StringComparison.Ordinal)
        .Replace("licence", "license", StringComparison.Ordinal);
}
