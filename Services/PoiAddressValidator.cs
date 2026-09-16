using System.Text.Json;
using System.Text.RegularExpressions;

namespace BusinessLicensing_Practice.Services;

// Deliberately limited to "premises, street" in Line1, or premises in Line1/street in Line2.
internal static class PoiAddressValidator
{
    private static string Normal(string value) => string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    private static string Field(JsonElement attributes, string name) =>
        attributes.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    private static (string Name, string Street)? Split(TradingAddress address)
    {
        var parts = address.Line1.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && string.IsNullOrWhiteSpace(address.Line2)
            && Normal(parts[0]).Length > 0 && Normal(parts[1]).Length > 0) return (parts[0], parts[1]);
        if (parts.Length == 1 && Normal(parts[0]).Length > 0 && !string.IsNullOrWhiteSpace(address.Line2))
            return (parts[0], address.Line2.Trim());
        return null;
    }
    public static bool HasNamedStreet(TradingAddress address) => Split(address) is not null;
    public static GeocodedPoint? Point(JsonElement candidate)
    {
        if (!candidate.TryGetProperty("location", out var location)
            || !location.TryGetProperty("x", out var x) || !location.TryGetProperty("y", out var y)
            || !x.TryGetDouble(out var longitude) || !y.TryGetDouble(out var latitude)) return null;
        return double.IsFinite(latitude) && latitude is >= -90 and <= 90
            && double.IsFinite(longitude) && longitude is >= -180 and <= 180 ? new(latitude, longitude) : null;
    }
    private static bool AddressMatches(TradingAddress input, string street, JsonElement a)
    {
        if (Normal(Field(a, "Country")) != "ZAF" || Normal(Field(a, "Region")) != "WESTERNCAPE") return false;
        var town = Normal(input.City);
        if (town.Length == 0 || (town != Normal(Field(a, "City")) && town != Normal(Field(a, "District")))) return false;
        var postal = Normal(Field(a, "Postal"));
        if (Normal(input.PostalCode).Length > 0 && postal.Length > 0 && postal != Normal(input.PostalCode)) return false;
        var returnedNumber = Field(a, "AddNum");
        var suppliedNumber = Regex.Match(street, @"^\s*(\d+[A-Za-z]?)\b");
        if (suppliedNumber.Success)
            return Normal(Field(a, "StAddr")) == Normal(street)
                && Normal(returnedNumber) == Normal(suppliedNumber.Groups[1].Value);
        // A named facility may omit its number, but the provider must supply a numbered street.
        return Normal(returnedNumber).Length > 0 && Normal(Field(a, "StName")).Length > 0
            && Normal(Field(a, "StName") + " " + Field(a, "StType")) == Normal(street)
            && Normal(Field(a, "StAddr")) == Normal(returnedNumber + " " + street);
    }
    public static GeocodedPoint? Validate(TradingAddress input, JsonElement candidates)
    {
        var parts = Split(input);
        if (parts is null || candidates.GetArrayLength() >= 50) return null; // Potentially truncated candidates.
        GeocodedPoint? selected = null;
        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("attributes", out var a)) return null;
            var type = Field(a, "Addr_type");
            if (type != "POI") continue;
            var name = Normal(Field(a, "PlaceName"));
            if (name.Length == 0 || (name != Normal(parts.Value.Name)
                && name != Normal(input.City + " " + parts.Value.Name))) return null;
            if (!AddressMatches(input, parts.Value.Street, a) || Point(candidate) is not { } point) return null;
            // Only exact duplicate POI locations are equivalent; do not choose between conflicting POIs.
            if (selected is not null && selected != point) return null;
            selected = point;
        }
        if (selected is null) return null;
        foreach (var candidate in candidates.EnumerateArray())
        {
            var a = candidate.GetProperty("attributes");
            if (Field(a, "Addr_type") is not ("PointAddress" or "Subaddress" or "StreetAddress")) continue;
            if (!AddressMatches(input, parts.Value.Street, a) || Point(candidate) is not { } point) return null;
            // Permit nearby entrance/interpolated representations of the SAME address, not remote locations.
            var lat = (point.Latitude - selected.Latitude) * Math.PI / 180;
            var lon = (point.Longitude - selected.Longitude) * Math.PI / 180;
            var h = Math.Pow(Math.Sin(lat / 2), 2) + Math.Cos(point.Latitude * Math.PI / 180)
                * Math.Cos(selected.Latitude * Math.PI / 180) * Math.Pow(Math.Sin(lon / 2), 2);
            if (6371000 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(h, 0, 1))) > 50) return null;
        }
        return selected;
    }
}
