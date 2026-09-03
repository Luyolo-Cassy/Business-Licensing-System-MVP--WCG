using System.Text.Json;
using BusinessLicensing_Practice.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace BusinessLicensing_Practice.Services
{
    public class ApplicationPdfService
    {
        private static readonly XColor BrandBlue = XColor.FromArgb(0, 91, 150);
        private static readonly XColor LightBlue = XColor.FromArgb(229, 241, 248);
        private static readonly XColor TextGrey = XColor.FromArgb(70, 70, 70);

        public byte[] Generate(Application application)
        {
            using var document = new PdfDocument();
            document.Info.Title = $"Business Licence Application {application.ApplicationNumber}";
            document.Info.Author = "Western Cape Government - DEDAT";

            var renderer = new PdfRenderer(document, application.ApplicationNumber);
            renderer.AddHeader(application);

            renderer.AddSection("Section A - Application / Applicant Information");
            renderer.AddRows(
            [
                ("Application reference", application.ApplicationNumber),
                ("Licence type", application.LicenceType),
                ("Application type", application.Details?.ApplicationType),
                ("Responsible municipality", application.Municipality),
                ("Applicant / owner", application.Details?.ApplicantName),
                ("Applicant address", application.Details?.ApplicantAddress),
                ("Telephone", application.Details?.ApplicantTelephone),
                ("Email", application.Details?.ApplicantEmail)
            ]);

            renderer.AddSection("Section B - Business Information");
            renderer.AddRows(
            [
                ("Business name", application.BusinessName),
                ("Registration number", application.RegistrationNumber),
                ("Tax number", application.TaxNumber),
                ("Nature of business", application.BusinessCategory),
                ("Business address", application.BusinessAddress),
                ("Postal address", application.Details?.PostalAddress),
                ("Town / city", application.City),
                ("Postal code", application.PostalCode),
                ("Contact person", application.Details?.ContactPerson),
                ("Business telephone", application.Details?.BusinessTelephone),
                ("Business email", application.Details?.BusinessEmail),
                ("Trading hours", application.Details?.TradingHours)
            ]);

            renderer.AddSection("Section C - Licence-Specific Information");
            var answers = DeserializeAnswers(application.Details?.LicenceSpecificDetailsJson);
            var definition = LicenceApplicationCatalog.Find(application.LicenceType);
            if (definition != null)
            {
                renderer.AddLicenceSpecificRows(definition.Questions.Select(question =>
                    (question.Label, answers.GetValueOrDefault(question.Key))));
            }

            renderer.AddSection("Supporting Documents");
            renderer.AddRows(application.Documents.Select(documentItem =>
                (documentItem.DocumentType, (string?)documentItem.FileName)));

            renderer.AddSection("Declaration");
            renderer.AddParagraph("I declare that the information supplied in this application is true and correct and that I am authorised to submit this application.");
            renderer.AddRows(
            [
                ("Declaration accepted", application.Details?.DeclarationAccepted == true ? "Yes" : "No"),
                ("Declaration date", application.Details?.DeclarationAcceptedAt?.ToString("dd MMMM yyyy HH:mm")),
                ("POPIA consent", application.PopiaConsentAccepted ? "Yes" : "No"),
                ("Submitted", application.DateSubmitted.ToString("dd MMMM yyyy HH:mm"))
            ]);

            renderer.Finish();

            using var stream = new MemoryStream();
            document.Save(stream, false);
            return stream.ToArray();
        }

        public static Dictionary<string, string> DeserializeAnswers(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<string, string>();
            }

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
            catch (JsonException)
            {
                return new Dictionary<string, string>();
            }
        }

        private sealed class PdfRenderer
        {
            private const double Margin = 42;
            private const double BottomMargin = 48;
            private readonly PdfDocument document;
            private readonly string reference;
            private readonly XFont regular = new("Arial", 9);
            private readonly XFont bold = new("Arial", 9, XFontStyleEx.Bold);
            private readonly XFont sectionFont = new("Arial", 12, XFontStyleEx.Bold);
            private readonly XFont titleFont = new("Arial", 16, XFontStyleEx.Bold);
            private PdfPage page = null!;
            private XGraphics graphics = null!;
            private double y;

            public PdfRenderer(PdfDocument document, string reference)
            {
                this.document = document;
                this.reference = reference;
                NewPage();
            }

            public void AddHeader(Application application)
            {
                graphics.DrawRectangle(new XSolidBrush(BrandBlue), 0, 0, page.Width.Point, 84);
                graphics.DrawString("WESTERN CAPE GOVERNMENT", titleFont, XBrushes.White, new XPoint(Margin, 35));
                graphics.DrawString("DEPARTMENT OF ECONOMIC DEVELOPMENT AND TOURISM", bold, XBrushes.White, new XPoint(Margin, 56));
                graphics.DrawString("PROVINCIAL BUSINESS LICENCE APPLICATION", bold, XBrushes.White, new XPoint(Margin, 72));
                y = 105;
            }

            public void AddSection(string title)
            {
                EnsureSpace(34);
                graphics.DrawRectangle(new XSolidBrush(LightBlue), Margin, y, page.Width.Point - (Margin * 2), 24);
                graphics.DrawString(title, sectionFont, new XSolidBrush(BrandBlue), new XPoint(Margin + 8, y + 17));
                y += 32;
            }

            public void AddRows(IEnumerable<(string Label, string? Value)> rows)
            {
                foreach (var (label, value) in rows)
                {
                    AddRow(label, value);
                }
            }

            public void AddLicenceSpecificRows(IEnumerable<(string Label, string? Value)> rows)
            {
                foreach (var (label, value) in rows)
                {
                    AddRow(label, value, 0.34);
                }
            }

            public void AddParagraph(string text)
            {
                var lines = Wrap(text, page.Width.Point - (Margin * 2), regular);
                EnsureSpace(lines.Count * 14 + 8);
                foreach (var line in lines)
                {
                    graphics.DrawString(line, regular, new XSolidBrush(TextGrey), new XPoint(Margin, y + 10));
                    y += 14;
                }
                y += 4;
            }

            private void AddRow(string label, string? value, double? labelColumnRatio = null)
            {
                var displayValue = string.IsNullOrWhiteSpace(value) ? "Not provided" : value.Trim();
                var contentWidth = page.Width.Point - (Margin * 2);
                var labelColumnWidth = labelColumnRatio.HasValue
                    ? contentWidth * labelColumnRatio.Value
                    : 170d;
                const double columnPadding = 12;
                var labelLines = Wrap(label, labelColumnWidth - columnPadding, bold);
                var valueLines = Wrap(displayValue, contentWidth - labelColumnWidth - columnPadding, regular);
                var lineCount = Math.Max(labelLines.Count, valueLines.Count);
                var height = Math.Max(22, lineCount * 13 + 8);
                EnsureSpace(height);

                graphics.DrawLine(new XPen(XColor.FromArgb(220, 220, 220)), Margin, y + height, page.Width.Point - Margin, y + height);
                for (var index = 0; index < labelLines.Count; index++)
                {
                    graphics.DrawString(labelLines[index], bold, new XSolidBrush(TextGrey), new XPoint(Margin, y + 14 + (index * 13)));
                }
                for (var index = 0; index < valueLines.Count; index++)
                {
                    graphics.DrawString(valueLines[index], regular, XBrushes.Black, new XPoint(Margin + labelColumnWidth, y + 14 + (index * 13)));
                }
                y += height;
            }

            private List<string> Wrap(string text, double width, XFont font)
            {
                var lines = new List<string>();
                var current = "";
                foreach (var word in text.Replace("\r", "").Replace("\n", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = string.IsNullOrEmpty(current) ? word : $"{current} {word}";
                    if (graphics.MeasureString(candidate, font).Width <= width)
                    {
                        current = candidate;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(current))
                        {
                            lines.Add(current);
                            current = "";
                        }

                        foreach (var character in word)
                        {
                            var fragment = $"{current}{character}";
                            if (graphics.MeasureString(fragment, font).Width <= width || string.IsNullOrEmpty(current))
                            {
                                current = fragment;
                            }
                            else
                            {
                                lines.Add(current);
                                current = character.ToString();
                            }
                        }
                    }
                }
                if (!string.IsNullOrEmpty(current)) lines.Add(current);
                if (lines.Count == 0) lines.Add("Not provided");
                return lines;
            }

            private void EnsureSpace(double requiredHeight)
            {
                if (y + requiredHeight <= page.Height.Point - BottomMargin) return;
                AddFooter();
                NewPage();
            }

            private void NewPage()
            {
                page = document.AddPage();
                page.Size = PdfSharp.PageSize.A4;
                graphics = XGraphics.FromPdfPage(page);
                y = Margin;
            }

            private void AddFooter()
            {
                var footer = $"Application {reference}  |  Page {document.Pages.Count}";
                graphics.DrawString(footer, regular, new XSolidBrush(TextGrey),
                    new XRect(Margin, page.Height.Point - 34, page.Width.Point - (Margin * 2), 16), XStringFormats.Center);
            }

            public void Finish() => AddFooter();
        }
    }
}
