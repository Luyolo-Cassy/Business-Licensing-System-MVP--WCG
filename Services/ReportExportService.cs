using System.Text;
using System.IO.Compression;
using System.Security;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Pdf;

namespace BusinessLicensing_Practice.Services;

public sealed class ReportExportService
{
    public byte[] Excel(ReportResult report)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Add(archive, "[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""");
            Add(archive, "_rels/.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Add(archive, "xl/workbook.xml", $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="{Xml(WorksheetName(report.Title))}" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Add(archive, "xl/_rels/workbook.xml.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
            Add(archive, "xl/styles.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs></styleSheet>""");
            Add(archive, "xl/worksheets/sheet1.xml", Worksheet(report));
        }
        return stream.ToArray();
    }

    public byte[] Pdf(ReportResult report, ReportFilter filter, DateTime generatedUtc)
    {
        using var document = new PdfDocument();
        var title = new XFont("Arial", 16, XFontStyleEx.Bold); var heading = new XFont("Arial", 9, XFontStyleEx.Bold); var normal = new XFont("Arial", 8);
        const double margin = 36; PdfPage page = null!; XGraphics gfx = null!; double y = 0, pageWidth = 0, pageHeight = 0;
        void NewPage() { gfx?.Dispose(); page = document.AddPage(); page.Size = PdfSharp.PageSize.A4; page.Orientation = PdfSharp.PageOrientation.Landscape; pageWidth = page.Width.Point; pageHeight = page.Height.Point; gfx = XGraphics.FromPdfPage(page); y = margin; }
        void Line(string text, XFont font, double gap) { if (y + gap > pageHeight - margin) NewPage(); gfx.DrawString(text, font, XBrushes.Black, new XRect(margin, y, pageWidth - margin * 2, gap), XStringFormats.TopLeft); y += gap; }
        void DrawTable(IReadOnlyList<ReportColumn> columns, IReadOnlyList<ReportRow> rows)
        {
            var width = (pageWidth - margin * 2) / Math.Max(1, columns.Count);
            void Draw(IEnumerable<string> values, XFont font, XBrush fill)
            {
                var cells = values.ToList(); var rowHeight = cells.Any(value => value.Length > 42) ? 42d : 22d;
                if (y + rowHeight > pageHeight - margin) { NewPage(); Draw(columns.Select(c => c.Heading), heading, XBrushes.LightGray); }
                var x = margin; var formatter = new XTextFormatter(gfx);
                foreach (var value in cells) { gfx.DrawRectangle(fill, x, y, width, rowHeight); gfx.DrawRectangle(XPens.Gray, x, y, width, rowHeight); formatter.DrawString(value, font, XBrushes.Black, new XRect(x + 3, y + 4, width - 6, rowHeight - 6)); x += width; } y += rowHeight;
            }
            Draw(columns.Select(c => c.Heading), heading, XBrushes.LightGray);
            if (rows.Count == 0) Draw(["No matching data"], normal, XBrushes.White); else foreach (var row in rows) Draw(columns.Select(c => row.Values.GetValueOrDefault(c.Key, "")), normal, XBrushes.White);
        }
        NewPage(); Line(report.Title, title, 25); Line($"Scope: {report.Scope}", heading, 16); Line($"Generated: {generatedUtc:yyyy-MM-dd HH:mm} UTC", normal, 14);
        var filters = FilterText(report.Key, filter); if (filters.Count > 0) Line("Filters: " + string.Join("; ", filters), normal, 18);
        Line(string.Join("    ", report.Summaries.Select(s => $"{s.Label}: {s.Value}")), heading, 20); DrawTable(report.Columns, report.Rows);
        if (report.SecondaryRows is { Count: > 0 } && report.SecondaryColumns != null) { y += 12; Line(report.SecondaryTitle ?? "Additional breakdown", heading, 20); DrawTable(report.SecondaryColumns, report.SecondaryRows); }
        gfx.Dispose(); using var stream = new MemoryStream(); document.Save(stream, false); return stream.ToArray();
    }

    public static string FileName(ReportResult report, string extension, DateTime date) => $"{Sanitize(report.Scope)}-{Sanitize(report.Title)}-{date:yyyy-MM-dd}.{extension}";
    private static string Worksheet(ReportResult report)
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><cols>");
        for (var i = 0; i < report.Columns.Count; i++) { var width = Math.Clamp(Math.Max(report.Columns[i].Heading.Length + 2, report.Rows.Select(r => r.Values.GetValueOrDefault(report.Columns[i].Key, "").Length).DefaultIfEmpty().Max() + 2), 12, 40); xml.Append($"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{width}\" customWidth=\"1\"/>"); }
        xml.Append("</cols><sheetData>");
        Row(xml, 1, [(report.Title, false)], true); Row(xml, 2, [($"Scope: {report.Scope}", false)], false); Row(xml, 4, report.Columns.Select(c => (c.Heading, false)), true);
        var rowNumber = 5; foreach (var row in report.Rows) Row(xml, rowNumber++, report.Columns.Select(c => { var value = row.Values.GetValueOrDefault(c.Key, ""); return (value, IsNumeric(c.Heading, value)); }), false);
        if (report.SecondaryRows is { Count: > 0 } && report.SecondaryColumns != null) { rowNumber++; Row(xml, rowNumber++, [(report.SecondaryTitle ?? "Additional breakdown", false)], true); Row(xml, rowNumber++, report.SecondaryColumns.Select(c => (c.Heading, false)), true); foreach (var row in report.SecondaryRows) Row(xml, rowNumber++, report.SecondaryColumns.Select(c => { var value = row.Values.GetValueOrDefault(c.Key, ""); return (value, IsNumeric(c.Heading, value)); }), false); }
        xml.Append("</sheetData></worksheet>"); return xml.ToString();
    }
    private static void Row(StringBuilder xml, int number, IEnumerable<(string Value, bool Numeric)> cells, bool bold)
    {
        xml.Append($"<row r=\"{number}\">"); var column = 0; foreach (var cell in cells) { var reference = ColumnName(++column) + number; if (cell.Numeric) xml.Append($"<c r=\"{reference}\" s=\"{(bold ? 1 : 0)}\"><v>{cell.Value}</v></c>"); else xml.Append($"<c r=\"{reference}\" t=\"inlineStr\" s=\"{(bold ? 1 : 0)}\"><is><t xml:space=\"preserve\">{Xml(cell.Value)}</t></is></c>"); } xml.Append("</row>");
    }
    private static bool IsNumeric(string heading, string value) => heading is "Applications" or "Total Applications" or "Pending" or "Licence Issued" or "Rejected" or "Withdrawn" or "Applications Received" or "Decisions Made" or "Decisions Measured" or "Currently Pending" or "Days Since Submission" or "Processing Days" or "Average Processing Days"
        && double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
    private static string ColumnName(int number) { var result = ""; while (number > 0) { number--; result = (char)('A' + number % 26) + result; number /= 26; } return result; }
    private static string WorksheetName(string value) { var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' }; var clean = new string(value.Where(c => !invalid.Contains(c)).ToArray()); return string.IsNullOrWhiteSpace(clean) ? "Report" : clean[..Math.Min(31, clean.Length)]; }
    private static string Xml(string value) => SecurityElement.Escape(value) ?? "";
    private static void Add(ZipArchive archive, string path, string contents) { var entry = archive.CreateEntry(path, CompressionLevel.Fastest); using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)); writer.Write(contents); }
    private static string Sanitize(string value) { var result = new string(value.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()); while (result.Contains("--")) result = result.Replace("--", "-"); return result.Trim('-'); }
    private static List<string> FilterText(string reportKey, ReportFilter f)
    {
        var values = new List<string>(); var prefix = reportKey is ReportKeys.ProcessingTime or ReportKeys.MunicipalityProcessing ? "Decision" : reportKey == ReportKeys.Processing ? "Period" : "Submitted";
        if (f.From != null) values.Add($"{prefix} From {f.From:yyyy-MM-dd}"); if (f.To != null) values.Add($"{prefix} To {f.To:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(f.Municipality)) values.Add($"Municipality: {f.Municipality}"); if (!string.IsNullOrWhiteSpace(f.LicenceType)) values.Add($"Licence: {f.LicenceType}");
        if (!string.IsNullOrWhiteSpace(f.ApplicationType)) values.Add($"Application type: {f.ApplicationType}"); if (!string.IsNullOrWhiteSpace(f.Status)) values.Add($"Status: {f.Status}");
        if (!string.IsNullOrWhiteSpace(f.Outcome)) values.Add($"Outcome: {f.Outcome}"); if (f.Grouping != "Monthly") values.Add($"Grouping: {f.Grouping}"); return values;
    }
}
