namespace BusinessLicensing_Practice.Services;

public enum ReportAudience { MunicipalOfficial, DedatAdmin }

public static class ReportKeys
{
    public const string Status = "status";
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Outcomes = "outcomes";
    public const string LicenceTypes = "licence-types";
    public const string ProcessingTime = "processing-time";
    public const string MunicipalityActivity = "municipality-activity";
    public const string MunicipalityProcessing = "municipality-processing";
    public const string Trends = "trends";
}

public sealed class ReportFilter
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Municipality { get; set; }
    public string? LicenceType { get; set; }
    public string? ApplicationType { get; set; }
    public string? Status { get; set; }
    public string? Outcome { get; set; }
    public string Grouping { get; set; } = "Monthly";
}

public sealed record ReportCard(string Key, string Name, string Description);
public sealed record ReportColumn(string Key, string Heading);
public sealed record ReportRow(IReadOnlyDictionary<string, string> Values);
public sealed record ReportChartSeries(string Name, IReadOnlyList<double> Values);
public sealed record ReportChart(string Type, IReadOnlyList<string> Labels, IReadOnlyList<ReportChartSeries> Series,
    bool Stacked = false, bool WholeNumbers = true);
public sealed record ReportSummary(string Label, string Value);
public sealed record ReportOptions(IReadOnlyList<string> Municipalities, IReadOnlyList<string> LicenceTypes,
    IReadOnlyList<string> ApplicationTypes, IReadOnlyList<string> Statuses, IReadOnlyList<string> Outcomes);

public sealed record ReportResult(string Key, string Title, string Description, string Scope,
    IReadOnlyList<ReportColumn> Columns, IReadOnlyList<ReportRow> Rows, ReportChart Chart,
    IReadOnlyList<ReportSummary> Summaries, ReportOptions Options, string? Insight = null,
    string? SecondaryTitle = null, IReadOnlyList<ReportRow>? SecondaryRows = null,
    IReadOnlyList<ReportColumn>? SecondaryColumns = null);
