using System.Globalization;
using System.Text.Json;

namespace BusinessLicensing_Practice.Services;

public sealed class WcgMunicipalBoundaryService(HttpClient client)
{
    public async Task<List<MunicipalityMatch>> FindMunicipalitiesAsync(GeocodedPoint point, CancellationToken cancellationToken = default)
    {
        const string name = "AFRIGIS_LocalMunicipalities.S12_NAME";
        const string code = "AFRIGIS_LocalMunicipalities.MUN_CODE";
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["f"] = "json", ["where"] = "1=1",
            ["geometry"] = point.Longitude.ToString("R", CultureInfo.InvariantCulture) + "," + point.Latitude.ToString("R", CultureInfo.InvariantCulture),
            ["geometryType"] = "esriGeometryPoint", ["inSR"] = "4326",
            ["spatialRel"] = "esriSpatialRelIntersects", ["outFields"] = name + "," + code,
            ["returnGeometry"] = "false"
        });
        // POST keeps the trading location out of request-URL logs. This is a read-only query.
        using var response = await client.PostAsync("https://gis.westerncape.gov.za/server2/rest/services/SpatialDataWarehouse/AfriGIS_Boundaries/MapServer/13/query", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;
        if (root.TryGetProperty("error", out _) ||
            (root.TryGetProperty("exceededTransferLimit", out var exceeded) && exceeded.GetBoolean()))
            throw new InvalidOperationException("Boundary service returned an incomplete result.");
        return root.GetProperty("features").EnumerateArray().Select(feature =>
        {
            var attributes = feature.GetProperty("attributes");
            var municipalityName = attributes.GetProperty(name).GetString();
            var municipalityCode = attributes.GetProperty(code).GetString();
            if (string.IsNullOrWhiteSpace(municipalityName) || string.IsNullOrWhiteSpace(municipalityCode))
                throw new InvalidOperationException("Boundary attributes are missing.");
            return new MunicipalityMatch(municipalityName, municipalityCode);
        }).ToList();
    }
}
