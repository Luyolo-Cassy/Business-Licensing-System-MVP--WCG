using System.Text.Json;

namespace BusinessLicensing_Practice.Services;

public sealed class ArcGisGeocodingService(HttpClient client, IConfiguration configuration)
{
    public async Task<GeocodedPoint?> GeocodeAsync(TradingAddress address, CancellationToken cancellationToken = default)
    {
        var key = configuration["ArcGIS:ApiKey"];
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Geocoding is not configured.");
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["f"] = "json", ["token"] = key, ["address"] = address.Line1,
            ["address2"] = address.Line2, ["neighborhood"] = address.Suburb,
            ["city"] = address.City, ["postal"] = address.PostalCode,
            ["region"] = "Western Cape", ["sourceCountry"] = "ZAF",
            ["outSR"] = "4326", ["outFields"] = "Addr_type", ["maxLocations"] = "1", ["forStorage"] = "false"
        });
        using var response = await client.PostAsync("https://geocode-api.arcgis.com/arcgis/rest/services/World/GeocodeServer/findAddressCandidates", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;
        if (root.TryGetProperty("error", out _)) throw new InvalidOperationException("Geocoding service error.");
        var candidates = root.GetProperty("candidates");
        if (candidates.GetArrayLength() == 0) return null;
        var candidate = candidates[0];
        if (string.IsNullOrWhiteSpace(candidate.GetProperty("address").GetString())) return null;
        var type = candidate.GetProperty("attributes").GetProperty("Addr_type").GetString();
        if (type is not ("PointAddress" or "Subaddress" or "StreetAddress")) return null;
        var location = candidate.GetProperty("location");
        var latitude = location.GetProperty("y").GetDouble();
        var longitude = location.GetProperty("x").GetDouble();
        return double.IsFinite(latitude) && latitude is >= -90 and <= 90
            && double.IsFinite(longitude) && longitude is >= -180 and <= 180
            ? new(latitude, longitude) : null;
    }
}
