using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;

namespace BusinessLicensing_Practice.Models;

public sealed class TradingDay
{
    public string Day { get; set; } = "";
    public bool? IsOpen { get; set; }
    public string OpeningTime { get; set; } = "";
    public string ClosingTime { get; set; } = "";
}

public static class ApplicationEntry
{
    public static readonly string[] Days = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    public static string? ValidateName(string? firstName, string? lastName)
    {
        if (string.IsNullOrWhiteSpace(firstName)) return "First Name is required.";
        if (string.IsNullOrWhiteSpace(lastName)) return "Last Name is required.";
        if (firstName.Trim().Length > 100 || lastName.Trim().Length > 100)
            return "First Name and Last Name must each be 100 characters or fewer.";
        return null;
    }

    public static string? ValidatePostalAddress(bool? sameAsBusiness, string? line1, string? suburb, string? city, string? postalCode)
    {
        if (sameAsBusiness == null) return "Please choose whether your postal address is the same as your Place of Business address.";
        if (sameAsBusiness == false && new[] { line1, suburb, city, postalCode }.Any(string.IsNullOrWhiteSpace))
            return "Please complete all required postal address fields.";
        return null;
    }

    public static bool ValidRegistration(string? value) =>
        Regex.IsMatch(value?.Trim() ?? "", @"^[0-9]{4}/[0-9]{6}/[0-9]{2}$", RegexOptions.CultureInvariant);

    public static bool ValidTaxNumber(string? value) =>
        Regex.IsMatch(value?.Trim() ?? "", @"^[0-9]{10}$", RegexOptions.CultureInvariant);

    public static string? ValidateTradingHours(IReadOnlyList<TradingDay> days)
    {
        if (days.Count != Days.Length || days.Where((day, index) => day.Day != Days[index]).Any())
            return "Please configure trading hours for every day.";
        if (days.Any(day => day.IsOpen == null)) return "Please choose Open or Closed for every trading day.";
        foreach (var day in days.Where(day => day.IsOpen == true))
        {
            if (string.IsNullOrWhiteSpace(day.OpeningTime) || string.IsNullOrWhiteSpace(day.ClosingTime))
                return $"Enter both opening and closing times for {day.Day} in HH:mm format.";
            if (!TryParseTradingTime(day.OpeningTime, out var opening) ||
                !TryParseTradingTime(day.ClosingTime, out var closing))
                return $"{day.Day}: Enter valid 24-hour times in HH:mm format (for example 08:00).";
            if (closing <= opening)
                return $"{day.Day}: Closing Time must be after Opening Time.";
        }
        return null;
    }

    private static bool TryParseTradingTime(string? value, out TimeOnly time)
    {
        time = default;
        return Regex.IsMatch(value ?? "", @"^(?:[01][0-9]|2[0-3]):[0-5][0-9]$", RegexOptions.CultureInvariant) &&
            TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    public static string SerializeTradingHours(IReadOnlyList<TradingDay> days) => JsonSerializer.Serialize(days);

    public static string FormatTradingHours(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return "";
        try
        {
            var days = JsonSerializer.Deserialize<List<TradingDay>>(stored);
            if (days != null && days.Count == Days.Length &&
                days.Where((day, index) => day.Day != Days[index]).Any() == false)
                return string.Join("\n", days.Select(day => day.IsOpen == true
                    ? $"{day.Day}: {day.OpeningTime} – {day.ClosingTime}"
                    : $"{day.Day}: Closed"));
        }
        catch (JsonException) { }
        return stored;
    }

    public static string FormatAddress(params string?[] parts) =>
        string.Join(", ", parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));

    public static string FullName(ApplicationDetails? details) => details == null ? "" :
        !string.IsNullOrWhiteSpace(details.ApplicantFirstName) || !string.IsNullOrWhiteSpace(details.ApplicantLastName)
            ? string.Join(" ", new[] { details.ApplicantFirstName, details.ApplicantLastName }
                .Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()))
            : details.ApplicantName ?? "";

    public static string ApplicantAddress(ApplicationDetails? details) => details == null ? "" :
        !string.IsNullOrWhiteSpace(details.ApplicantAddressLine1)
            ? FormatAddress(details.ApplicantAddressLine1, details.ApplicantAddressLine2,
                details.ApplicantSuburb, details.ApplicantCity, details.ApplicantPostalCode)
            : details.ApplicantAddress ?? "";

    public static string PlaceOfBusinessAddress(Application application) =>
        !string.IsNullOrWhiteSpace(application.PlaceOfBusinessAddressLine1)
            ? FormatAddress(application.PlaceOfBusinessAddressLine1, application.PlaceOfBusinessAddressLine2,
                application.PlaceOfBusinessSuburb, application.PlaceOfBusinessCity, application.PlaceOfBusinessPostalCode)
            : application.PlaceOfBusinessAddress;

    public static string PostalAddress(Application application)
    {
        var details = application.Details;
        if (details == null) return "";
        if (details.PostalAddressSameAsBusiness == true) return PlaceOfBusinessAddress(application);
        if (details.PostalAddressSameAsBusiness == false)
            return FormatAddress(details.PostalAddressLine1, details.PostalAddressLine2,
                details.PostalSuburb, details.PostalCity, details.PostalPostalCode);
        return details.PostalAddress ?? "";
    }
}
