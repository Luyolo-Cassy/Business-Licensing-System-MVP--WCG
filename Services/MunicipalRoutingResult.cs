namespace BusinessLicensing_Practice.Services;

public sealed record TradingAddress(string Line1, string Line2, string Suburb, string City, string PostalCode)
{
    public string Formatted => string.Join(", ", new[] { Line1, Line2, Suburb, City, PostalCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
public sealed record GeocodedPoint(double Latitude, double Longitude);
public sealed record MunicipalityMatch(string Name, string Code);
public enum RoutingFailure { None, AddressNotPrecise, Unavailable, NoMunicipality, MultipleMunicipalities, Unsupported }
public sealed record MunicipalRoutingResult(string? Municipality, RoutingFailure Failure, string? BoundaryName = null)
{
    public bool Success => Failure == RoutingFailure.None && Municipality is not null;
    public string Message => Failure switch
    {
        RoutingFailure.AddressNotPrecise => "We couldn’t locate your exact trading address. Please check the street number, street name, town and postal code.",
        RoutingFailure.NoMunicipality => "We couldn’t determine the municipality for this trading address. Please check the address.",
        RoutingFailure.MultipleMunicipalities => "We couldn’t determine a single municipality. Please check the exact trading address.",
        RoutingFailure.Unsupported => $"This address falls within {BoundaryName}. Online applications currently support Hessequa, Bergrivier, Cederberg, Swartland and Witzenberg only.",
        _ => "Address verification is temporarily unavailable. Your application has not been submitted. Please try again."
    };
}
