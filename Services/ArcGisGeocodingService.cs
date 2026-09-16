using System.Text.Json;

namespace BusinessLicensing_Practice.Services;

public sealed class ArcGisGeocodingService(HttpClient client, IConfiguration configuration)
{
    public async Task<GeocodedPoint?> GeocodeAsync(TradingAddress address, CancellationToken cancellationToken = default)
    {
        var key = configuration["ArcGIS:ApiKey"];
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Geocoding is not configured.");
        using var json = await RequestAsync(new Dictionary<string, string>
        {
            ["f"] = "json", ["token"] = key, ["address"] = address.Line1,
            ["address2"] = address.Line2, ["neighborhood"] = address.Suburb,
            ["city"] = address.City, ["postal"] = address.PostalCode,
            ["region"] = "Western Cape", ["sourceCountry"] = "ZAF",
            ["outSR"] = "4326", ["outFields"] = "Addr_type", ["maxLocations"] = "1", ["forStorage"] = "false"
        }, cancellationToken);
        var candidates = json.RootElement.GetProperty("candidates");
        if (candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];
            var type = candidate.GetProperty("attributes").GetProperty("Addr_type").GetString();
            if (type is "PointAddress" or "Subaddress" or "StreetAddress"
                && !string.IsNullOrWhiteSpace(candidate.GetProperty("address").GetString())
                && PoiAddressValidator.Point(candidate) is { } precise)
                return precise;
        }
        if (!PoiAddressValidator.HasNamedStreet(address)) return null;
        using var fallback = await RequestAsync(new Dictionary<string, string>
        {
            ["f"] = "json", ["token"] = key,
            ["SingleLine"] = address.Formatted + ", Western Cape, South Africa",
            ["sourceCountry"] = "ZAF", ["outSR"] = "4326", ["forStorage"] = "false",
            ["maxLocations"] = "50",
            ["outFields"] = "Addr_type,PlaceName,StAddr,AddNum,StName,StType,City,District,Postal,Region,Country"
        }, cancellationToken);
        return PoiAddressValidator.Validate(address, fallback.RootElement.GetProperty("candidates"));
    }

    private async Task<JsonDocument> RequestAsync(Dictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        using var body = new FormUrlEncodedContent(parameters);
        using var response = await client.PostAsync("https://geocode-api.arcgis.com/arcgis/rest/services/World/GeocodeServer/findAddressCandidates", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (json.RootElement.TryGetProperty("error", out _))
        {
            json.Dispose();
            throw new InvalidOperationException("Geocoding service error.");
        }
        return json;
    }
}
