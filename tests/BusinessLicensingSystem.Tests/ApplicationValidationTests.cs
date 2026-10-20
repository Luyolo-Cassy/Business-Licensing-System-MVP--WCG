using BusinessLicensing_Practice.Models;

namespace BusinessLicensingSystem.Tests.ApplicationValidation;

[Trait("Feature", "Application Validation")]
public sealed class ApplicationValidationTests
{
    [Fact]
    public void ValidRegistrationNumber_IsAccepted() =>
        Assert.True(ApplicationEntry.ValidRegistration("2024/123456/07"));

    [Fact]
    public void InvalidRegistrationNumber_IsRejected() =>
        Assert.False(ApplicationEntry.ValidRegistration("2024-123456-07"));

    [Fact]
    public void TaxNumber_PreservesAndAcceptsLeadingZero() =>
        Assert.True(ApplicationEntry.ValidTaxNumber("0123456789"));

    [Fact]
    public void InvalidEmailAddress_IsRejected() =>
        Assert.False(ApplicationEntry.ValidEmail("not-an-email"));

    [Fact]
    public void ClosingBeforeOpening_IsRejected()
    {
        var days = ApplicationEntry.Days.Select(day => new TradingDay
        {
            Day = day, IsOpen = false
        }).ToList();
        days[0].IsOpen = true;
        days[0].OpeningTime = "17:00";
        days[0].ClosingTime = "08:00";

        Assert.Contains("Closing Time", ApplicationEntry.ValidateTradingHours(days));
    }
}
