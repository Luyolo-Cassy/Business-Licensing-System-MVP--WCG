using System.IO.Compression;
using System.Xml.Linq;
using BusinessLicensing_Practice.Services;

namespace BusinessLicensingSystem.Tests.Reporting;

[Trait("Feature", "Reporting")]
public sealed class ReportingTests
{
    private readonly ReportExportService exports = new();

    [Fact]
    public void ExcelExport_IsAValidZipWorkbook()
    {
        var bytes = exports.Excel(Report());
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
    }

    [Fact]
    public void ExcelExport_ContainsWorkbookAndWorksheetParts()
    {
        using var archive = OpenWorkbook();
        Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
        Assert.NotNull(archive.GetEntry("xl/worksheets/sheet1.xml"));
    }

    [Fact]
    public void ExcelExport_WritesSeparateColumnHeadings()
    {
        using var archive = OpenWorkbook();
        var sheet = ReadSheet(archive);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var headings = sheet.Descendants(ns + "row").Single(row => (string?)row.Attribute("r") == "4").Elements(ns + "c").ToList();
        Assert.Equal(["Status", "Applications"], headings.Select(cell => cell.Value));
    }

    [Fact]
    public void ExcelExport_WritesApplicationCountAsNumericCell()
    {
        using var archive = OpenWorkbook();
        var sheet = ReadSheet(archive);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var countCell = sheet.Descendants(ns + "c").Single(cell => (string?)cell.Attribute("r") == "B5");
        Assert.Null(countCell.Attribute("t"));
        Assert.Equal("3", countCell.Element(ns + "v")?.Value);
    }

    [Fact]
    public void ReportFileName_IsSanitisedAndDated()
    {
        var name = ReportExportService.FileName(Report(), "xlsx", new DateTime(2026, 10, 20));
        Assert.Equal("Hessequa-Municipality-Application-Status-2026-10-20.xlsx", name);
    }

    private ZipArchive OpenWorkbook() => new(new MemoryStream(exports.Excel(Report())), ZipArchiveMode.Read);

    private static XDocument ReadSheet(ZipArchive archive)
    {
        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        return XDocument.Parse(reader.ReadToEnd());
    }

    private static ReportResult Report() => new(ReportKeys.Status, "Application Status", "Status totals", "Hessequa Municipality",
        [new("Status", "Status"), new("Applications", "Applications")],
        [new(new Dictionary<string, string> { ["Status"] = "Under Review", ["Applications"] = "3" })],
        new("bar", ["Under Review"], [new("Applications", [3])]),
        [new("Total", "3")], new([], [], [], [], []));
}
