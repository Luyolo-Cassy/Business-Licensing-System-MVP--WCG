using System.Globalization;
using System.Security.Claims;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessLicensing_Practice.Services;

public sealed class ReportingService(IServiceScopeFactory scopes)
{
    public static readonly string[] WorkflowStatuses = ["Submitted", "Under Review", "Department Assessment", "Final Decision", "Licence Issued", "Rejected", "Withdrawn"];
    public static readonly string[] PendingStatuses = ["Submitted", "Under Review", "Department Assessment", "Final Decision"];
    public static readonly string[] Outcomes = ["Licence Issued", "Rejected", "Withdrawn"];
    public static readonly string[] DecisionOutcomes = ["Licence Issued", "Rejected"];

    public static IReadOnlyList<ReportCard> Cards(ReportAudience audience) => audience == ReportAudience.MunicipalOfficial
        ? [
            new(ReportKeys.Status, "Application Status Report", "See the current stage of applications submitted during the selected period."),
            new(ReportKeys.Pending, "Pending Applications Report", "See applications that are still being processed and how long it has been since they were submitted."),
            new(ReportKeys.Processing, "Application Processing Report", "Compare applications received with municipal decisions made over time."),
            new(ReportKeys.Outcomes, "Application Outcome Report", "See issued licences, rejected applications and applicant withdrawals."),
            new(ReportKeys.LicenceTypes, "Licence Type Report", "See which types of business licences receive the most applications."),
            new(ReportKeys.ProcessingTime, "Processing Time Report", "See how long applications took from submission to a municipal decision.")]
        : [
            new(ReportKeys.Status, "Provincial Licensing Overview", "See the current distribution of applications across the licensing process."),
            new(ReportKeys.MunicipalityActivity, "Municipality Application Overview", "Compare application volumes and current application states across municipalities."),
            new(ReportKeys.MunicipalityProcessing, "Municipality Processing Report", "Compare municipal decisions, current pending workload and processing times across municipalities."),
            new(ReportKeys.Trends, "Provincial Application Trends Report", "See how many licence applications are submitted across the province over time."),
            new(ReportKeys.LicenceTypes, "Provincial Licence Demand Report", "Compare demand for different licence types across municipalities."),
            new(ReportKeys.Outcomes, "Provincial Application Outcome Report", "See issued licences, rejected applications and applicant withdrawals across the province.")];

    public async Task<ReportResult> GenerateAsync(ClaimsPrincipal principal, ReportAudience audience, string key, ReportFilter filter)
    {
        if (filter.From?.Date > filter.To?.Date) throw new ArgumentException("From date must be on or before To date.");
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        IQueryable<Application> authorized = db.Applications.AsNoTracking(); string scopeName;
        if (audience == ReportAudience.MunicipalOfficial)
        {
            var official = await OfficialAccess.GetAsync(db, principal) ?? throw new UnauthorizedAccessException();
            scopeName = official.Municipality!; authorized = authorized.Where(a => a.Municipality == official.Municipality); filter.Municipality = null;
        }
        else
        {
            var admin = await users.GetUserAsync(principal);
            if (admin == null || !await users.IsInRoleAsync(admin, "DEDATAdmin")) throw new UnauthorizedAccessException();
            scopeName = string.IsNullOrWhiteSpace(filter.Municipality) ? "Province-wide" : filter.Municipality;
            if (!string.IsNullOrWhiteSpace(filter.Municipality))
            {
                if (filter.Municipality == "Not assigned") authorized = authorized.Where(a => a.Municipality == null || a.Municipality == "" || a.Municipality.Trim() == "");
                else authorized = authorized.Where(a => a.Municipality == filter.Municipality);
            }
        }

        var optionRows = await authorized.Select(a => new { a.Municipality, a.LicenceType, a.Status, ApplicationType = a.Details != null ? a.Details.ApplicationType : null }).ToListAsync();
        var municipalityOptions = await db.Municipalities.AsNoTracking().Select(m => m.Name).ToListAsync();
        municipalityOptions = municipalityOptions.Union(optionRows.Select(x => Name(x.Municipality, "Not assigned"))).OrderBy(x => x).ToList();
        var options = new ReportOptions(municipalityOptions,
            LicenceApplicationCatalog.Licences.Select(x => x.Name).Union(optionRows.Select(x => Name(x.LicenceType))).OrderBy(x => x).ToList(),
            optionRows.Select(x => Name(x.ApplicationType)).Distinct().OrderBy(x => x).ToList(),
            WorkflowStatuses.Union(optionRows.Select(x => Name(x.Status))).ToList(), Outcomes);

        if (!string.IsNullOrWhiteSpace(filter.LicenceType)) authorized = authorized.Where(a => a.LicenceType == filter.LicenceType);
        if (!string.IsNullOrWhiteSpace(filter.ApplicationType)) authorized = authorized.Where(a => a.Details != null && a.Details.ApplicationType == filter.ApplicationType);
        if (!string.IsNullOrWhiteSpace(filter.Status)) authorized = authorized.Where(a => a.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.Outcome)) authorized = authorized.Where(a => a.Status == filter.Outcome);
        var data = await authorized.Select(a => new ReportingApplication(a.ApplicationNumber, a.BusinessName, a.LicenceType, a.Status,
            a.Municipality, a.DateSubmitted, a.DecisionDateUtc, a.DecisionReason, a.Details != null ? a.Details.ApplicationType : null)).ToListAsync();
        return Build(audience, key, filter, scopeName, data, options, municipalityOptions);
    }

    private static ReportResult Build(ReportAudience audience, string key, ReportFilter filter, string scope, List<ReportingApplication> all, ReportOptions options, List<string> municipalities)
    {
        var card = Cards(audience).SingleOrDefault(c => c.Key == key) ?? throw new ArgumentException("Unknown report.");
        List<ReportingApplication> Submitted() => all.Where(x => InRange(x.DateSubmitted, filter)).ToList();
        return key switch
        {
            ReportKeys.Status => CategoryReport(card, scope, Submitted(), options, x => Name(x.Status), "Status", "doughnut"),
            ReportKeys.Pending => PendingReport(card, scope, Submitted(), options),
            ReportKeys.Processing => ProcessingReport(card, scope, all, options, filter),
            ReportKeys.Outcomes => OutcomeReport(card, scope, Submitted(), options),
            ReportKeys.LicenceTypes => CategoryReport(card, scope, Submitted(), options, x => Name(x.LicenceType), "Licence Type", "bar", licence: true),
            ReportKeys.ProcessingTime => ProcessingTimeReport(card, scope, all.Where(x => x.DecisionDateUtc != null && InRange(x.DecisionDateUtc.Value, filter)).ToList(), options),
            ReportKeys.Trends => TrendsReport(card, scope, Submitted(), options, filter),
            ReportKeys.MunicipalityActivity => MunicipalityActivity(card, scope, Submitted(), options, municipalities),
            ReportKeys.MunicipalityProcessing => MunicipalityProcessing(card, scope, all, options, municipalities, filter),
            _ => throw new ArgumentException("Unknown report.")
        };
    }

    private static ReportResult CategoryReport(ReportCard card, string scope, List<ReportingApplication> data, ReportOptions options,
        Func<ReportingApplication, string> selector, string category, string chartType, bool licence = false)
    {
        var counts = data.GroupBy(selector).Select(g => (Name: g.Key, Count: g.Count())).Where(x => x.Count > 0).OrderBy(x => x.Name).ToList();
        var rows = counts.Select(x => Row((category, x.Name), ("Applications", x.Count.ToString()))).ToList();
        var summaries = new List<ReportSummary> { new("Total Applications", data.Count.ToString()) };
        string? insight = null;
        if (licence && counts.Count > 0) { var top = counts.OrderByDescending(x => x.Count).ThenBy(x => x.Name).First(); summaries.Add(new("Most Applied-For Licence", top.Name)); insight = $"{top.Name} was the most applied-for licence type with {top.Count} application{Plural(top.Count)}."; }
        return Result(card, scope, options, rows, [Col(category), Col("Applications")], chartType, counts.Select(x => x.Name), [new("Applications", counts.Select(x => (double)x.Count).ToList())], summaries, insight);
    }

    private static ReportResult PendingReport(ReportCard card, string scope, List<ReportingApplication> data, ReportOptions options)
    {
        var now = DateTime.Now; var pending = data.Where(x => PendingStatuses.Contains(x.Status)).OrderBy(x => x.DateSubmitted).ToList();
        int Days(ReportingApplication x) => Math.Max(0, (int)(now - x.DateSubmitted).TotalDays);
        var rows = pending.Select(x => Row(("Application Number", x.Number), ("Business Name", x.BusinessName), ("Licence Type", Name(x.LicenceType)),
            ("Current Status", Name(x.Status)), ("Date Submitted", x.DateSubmitted.ToString("yyyy-MM-dd")), ("Days Since Submission", Days(x).ToString()))).ToList();
        string[] brackets = ["0-7 days", "8-14 days", "15-30 days", "31-60 days", "61+ days"];
        double[] values = [pending.Count(x => Days(x) <= 7), pending.Count(x => Days(x) is >= 8 and <= 14), pending.Count(x => Days(x) is >= 15 and <= 30), pending.Count(x => Days(x) is >= 31 and <= 60), pending.Count(x => Days(x) >= 61)];
        var over30 = pending.Count(x => Days(x) > 30); var insight = over30 == 0 ? null : $"{over30} application{Plural(over30)} have been waiting more than 30 days since submission.";
        return Result(card, scope, options, rows, [Col("Application Number"), Col("Business Name"), Col("Licence Type"), Col("Current Status"), Col("Date Submitted"), Col("Days Since Submission")],
            "bar", brackets, [new("Applications by time since submission", values)], [new("Currently Pending", pending.Count.ToString()), new("Pending More Than 30 Days", over30.ToString())], insight);
    }

    private static ReportResult ProcessingReport(ReportCard card, string scope, List<ReportingApplication> all, ReportOptions options, ReportFilter filter)
    {
        var received = all.Where(x => InRange(x.DateSubmitted, filter)).ToList();
        var decisions = all.Where(x => x.DecisionDateUtc != null && DecisionOutcomes.Contains(x.Status) && InRange(x.DecisionDateUtc.Value, filter)).ToList();
        var periods = Periods(filter, received.Select(x => x.DateSubmitted).Concat(decisions.Select(x => x.DecisionDateUtc!.Value)));
        var quarterly = IsQuarterly(filter); string Label(DateTime d) => PeriodLabel(d, quarterly); DateTime Next(DateTime d) => d.AddMonths(quarterly ? 3 : 1);
        var rows = new List<ReportRow>(); var receivedValues = new List<double>(); var decisionValues = new List<double>();
        foreach (var period in periods) { var end = Next(period); var r = received.Count(x => x.DateSubmitted >= period && x.DateSubmitted < end); var d = decisions.Count(x => x.DecisionDateUtc >= period && x.DecisionDateUtc < end); receivedValues.Add(r); decisionValues.Add(d); rows.Add(Row(("Period", Label(period)), ("Applications Received", r.ToString()), ("Decisions Made", d.ToString()))); }
        var insight = received.Count == 0 && decisions.Count == 0 ? null : $"{received.Count} application{Plural(received.Count)} were received and {decisions.Count} municipal decision{Plural(decisions.Count)} were made during the selected period.";
        return Result(card, scope, options, rows, [Col("Period"), Col("Applications Received"), Col("Decisions Made")], "bar", periods.Select(Label),
            [new("Applications Received", receivedValues), new("Decisions Made", decisionValues)], [new("Applications Received", received.Count.ToString()), new("Decisions Made", decisions.Count.ToString())], insight);
    }

    private static ReportResult OutcomeReport(ReportCard card, string scope, List<ReportingApplication> data, ReportOptions options)
    {
        var terminal = data.Where(x => Outcomes.Contains(x.Status)).ToList();
        var counts = Outcomes.Select(x => (Name: x, Count: terminal.Count(a => a.Status == x))).Where(x => x.Count > 0).ToList();
        var rows = counts.Select(x => Row(("Outcome", x.Name), ("Applications", x.Count.ToString()))).ToList();
        var rejected = terminal.Where(x => x.Status == "Rejected").ToList();
        var reasons = rejected.GroupBy(x => string.IsNullOrWhiteSpace(x.DecisionReason) ? "Not recorded" : x.DecisionReason.Trim()).OrderByDescending(x => x.Count())
            .Select(x => Row(("Rejection Reason", x.Key), ("Applications", x.Count().ToString()))).ToList();
        var result = Result(card, scope, options, rows, [Col("Outcome"), Col("Applications")], "doughnut", counts.Select(x => x.Name),
            [new("End-state applications", counts.Select(x => (double)x.Count).ToList())], [new("End-State Applications", terminal.Count.ToString())]);
        return result with { SecondaryTitle = "Recorded Rejection Reasons", SecondaryRows = reasons, SecondaryColumns = [Col("Rejection Reason"), Col("Applications")] };
    }

    private static ReportResult ProcessingTimeReport(ReportCard card, string scope, List<ReportingApplication> data, ReportOptions options)
    {
        var decisions = data.Where(x => DecisionOutcomes.Contains(x.Status)).OrderByDescending(x => x.DecisionDateUtc).ToList();
        double? Days(ReportingApplication x) { var days = (x.DecisionDateUtc!.Value - x.DateSubmitted).TotalDays; return days < 0 ? null : days; }
        var rows = decisions.Select(x => Row(("Application Number", x.Number), ("Licence Type", Name(x.LicenceType)), ("Date Submitted", x.DateSubmitted.ToString("yyyy-MM-dd")),
            ("Decision Date", x.DecisionDateUtc!.Value.ToString("yyyy-MM-dd")), ("Outcome", x.Status), ("Processing Days", Days(x)?.ToString("0.0", CultureInfo.InvariantCulture) ?? "N/A"))).ToList();
        var valid = decisions.Where(x => Days(x) != null).ToList(); var averages = valid.GroupBy(x => Name(x.LicenceType)).OrderBy(x => x.Key).Select(g => (g.Key, Value: g.Average(x => Days(x)!.Value))).ToList();
        var average = valid.Count == 0 ? (double?)null : valid.Average(x => Days(x)!.Value); var insight = average == null ? null : $"Applications with recorded decisions took an average of {average:0.0} days to process.";
        return Result(card, scope, options, rows, [Col("Application Number"), Col("Licence Type"), Col("Date Submitted"), Col("Decision Date"), Col("Outcome"), Col("Processing Days")],
            "bar", averages.Select(x => x.Key), [new("Average Processing Days", averages.Select(x => x.Value).ToList())],
            [new("Decisions Recorded", decisions.Count.ToString()), new("Average Processing Time", average == null ? "N/A" : average.Value.ToString("0.0", CultureInfo.InvariantCulture) + " days")], insight, wholeNumbers: false);
    }

    private static ReportResult TrendsReport(ReportCard card, string scope, List<ReportingApplication> data, ReportOptions options, ReportFilter filter)
    {
        var periods = Periods(filter, data.Select(x => x.DateSubmitted)); var quarterly = IsQuarterly(filter); string Label(DateTime d) => PeriodLabel(d, quarterly); DateTime Next(DateTime d) => d.AddMonths(quarterly ? 3 : 1);
        var counts = periods.Select(p => data.Count(x => x.DateSubmitted >= p && x.DateSubmitted < Next(p))).ToList();
        var rows = periods.Select((p, i) => Row(("Period", Label(p)), ("Applications Received", counts[i].ToString()))).ToList();
        return Result(card, scope, options, rows, [Col("Period"), Col("Applications Received")], "line", periods.Select(Label), [new("Applications Received", counts.Select(x => (double)x).ToList())], [new("Applications Received", data.Count.ToString())]);
    }

    private static ReportResult MunicipalityActivity(ReportCard card, string scope, List<ReportingApplication> data, ReportOptions options, List<string> all)
    {
        var names = (scope == "Province-wide" ? all : data.Select(x => Name(x.Municipality, "Not assigned"))).Union(data.Select(x => Name(x.Municipality, "Not assigned"))).Distinct().OrderBy(x => x).ToList();
        var groups = names.Select(n => (Name: n, Set: data.Where(x => Name(x.Municipality, "Not assigned") == n).ToList())).Where(x => x.Set.Count > 0).ToList();
        var rows = groups.Select(g => Row(("Municipality", g.Name), ("Total Applications", g.Set.Count.ToString()), ("Pending", g.Set.Count(x => PendingStatuses.Contains(x.Status)).ToString()),
            ("Licence Issued", g.Set.Count(x => x.Status == "Licence Issued").ToString()), ("Rejected", g.Set.Count(x => x.Status == "Rejected").ToString()), ("Withdrawn", g.Set.Count(x => x.Status == "Withdrawn").ToString()))).ToList();
        double[] Counts(Func<ReportingApplication, bool> match) => groups.Select(g => (double)g.Set.Count(match)).ToArray(); var pending = data.Count(x => PendingStatuses.Contains(x.Status));
        return Result(card, scope, options, rows, [Col("Municipality"), Col("Total Applications"), Col("Pending"), Col("Licence Issued"), Col("Rejected"), Col("Withdrawn")], "bar", groups.Select(x => x.Name),
            [new("Pending", Counts(x => PendingStatuses.Contains(x.Status))), new("Licence Issued", Counts(x => x.Status == "Licence Issued")), new("Rejected", Counts(x => x.Status == "Rejected")), new("Withdrawn", Counts(x => x.Status == "Withdrawn"))],
            [new("Total Applications", data.Count.ToString()), new("Currently Pending", pending.ToString())], stacked: true);
    }

    private static ReportResult MunicipalityProcessing(ReportCard card, string scope, List<ReportingApplication> all, ReportOptions options, List<string> municipalities, ReportFilter filter)
    {
        var decisions = all.Where(x => x.DecisionDateUtc != null && DecisionOutcomes.Contains(x.Status) && InRange(x.DecisionDateUtc.Value, filter)).ToList();
        var pending = all.Where(x => PendingStatuses.Contains(x.Status)).ToList();
        var names = (scope == "Province-wide" ? municipalities : all.Select(x => Name(x.Municipality, "Not assigned"))).Union(decisions.Select(x => Name(x.Municipality, "Not assigned"))).Union(pending.Select(x => Name(x.Municipality, "Not assigned"))).Distinct().OrderBy(x => x).ToList();
        double? Duration(ReportingApplication x) { var days = (x.DecisionDateUtc!.Value - x.DateSubmitted).TotalDays; return days < 0 ? null : days; }
        var metrics = names.Select(n => { var made = decisions.Where(x => Name(x.Municipality, "Not assigned") == n).ToList(); var measured = made.Where(x => Duration(x) != null).ToList(); return new { Name = n, Made = made.Count, Measured = measured.Count, Average = measured.Count == 0 ? (double?)null : measured.Average(x => Duration(x)!.Value), Pending = pending.Count(x => Name(x.Municipality, "Not assigned") == n) }; }).Where(x => x.Made > 0 || x.Pending > 0).ToList();
        var rows = metrics.Select(x => Row(("Municipality", x.Name), ("Decisions Made", x.Made.ToString()), ("Decisions Measured", x.Measured.ToString()),
            ("Average Processing Days", x.Average?.ToString("0.0", CultureInfo.InvariantCulture) ?? "N/A"), ("Currently Pending", x.Pending.ToString()))).ToList();
        var chart = metrics.Where(x => x.Average != null).ToList();
        return Result(card, scope, options, rows, [Col("Municipality"), Col("Decisions Made"), Col("Decisions Measured"), Col("Average Processing Days"), Col("Currently Pending")], "bar", chart.Select(x => x.Name),
            [new("Average Processing Days", chart.Select(x => x.Average!.Value).ToList())], [new("Decisions Made", decisions.Count.ToString()), new("Decisions Measured", decisions.Count(x => Duration(x) != null).ToString()), new("Currently Pending", pending.Count.ToString())], wholeNumbers: false);
    }

    private static ReportResult Result(ReportCard card, string scope, ReportOptions options, IReadOnlyList<ReportRow> rows, IReadOnlyList<ReportColumn> columns,
        string chartType, IEnumerable<string> labels, IReadOnlyList<ReportChartSeries> series, IReadOnlyList<ReportSummary> summaries, string? insight = null, bool stacked = false, bool wholeNumbers = true) =>
        new(card.Key, card.Name, card.Description, scope, columns, rows, new(chartType, labels.ToList(), series, stacked, wholeNumbers), summaries, options, insight);
    private static bool InRange(DateTime value, ReportFilter filter) => (filter.From == null || value >= filter.From.Value.Date) && (filter.To == null || value < filter.To.Value.Date.AddDays(1));
    private static bool IsQuarterly(ReportFilter filter) => string.Equals(filter.Grouping, "Quarterly", StringComparison.OrdinalIgnoreCase);
    private static List<DateTime> Periods(ReportFilter filter, IEnumerable<DateTime> events)
    {
        var values = events.ToList(); var quarterly = IsQuarterly(filter);
        DateTime Start(DateTime d) => quarterly ? new(d.Year, ((d.Month - 1) / 3) * 3 + 1, 1) : new(d.Year, d.Month, 1);
        var first = Start(filter.From?.Date ?? (values.Count > 0 ? values.Min() : DateTime.Today)); var last = Start(filter.To?.Date ?? (values.Count > 0 ? values.Max() : DateTime.Today));
        var periods = new List<DateTime>(); for (var p = first; p <= last; p = p.AddMonths(quarterly ? 3 : 1)) periods.Add(p); return periods;
    }
    private static string PeriodLabel(DateTime value, bool quarterly) => quarterly ? $"{value.Year} Q{((value.Month - 1) / 3) + 1}" : value.ToString("yyyy MMM", CultureInfo.InvariantCulture);
    private static string Plural(int count) => count == 1 ? "" : "s";
    private static ReportColumn Col(string name) => new(name, name);
    private static ReportRow Row(params (string Key, string Value)[] cells) => new(cells.ToDictionary(x => x.Key, x => x.Value));
    private static string Name(string? value, string missing = "Not recorded") => string.IsNullOrWhiteSpace(value) ? missing : value.Trim();
    private sealed record ReportingApplication(string Number, string BusinessName, string LicenceType, string Status, string? Municipality, DateTime DateSubmitted, DateTime? DecisionDateUtc, string? DecisionReason, string? ApplicationType);
}
