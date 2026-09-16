using System.Net;
using System.Text.Json;
using BusinessLicensing_Practice.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS: " + message);
}
var address = new TradingAddress("1 Church Street", "", "Central", "Malmesbury", "7300");
async Task<MunicipalRoutingResult> Run(string type, string[] names, string geocodeOverride = "", bool configured = true, bool networkError = false)
{
    var geocode = new Handler(async request =>
    {
        var body = await request.Content!.ReadAsStringAsync();
        Check(body.Contains("city=Malmesbury") && body.Contains("region=Western+Cape") && body.Contains("outFields=Addr_type"), "Structured address and precision requested");
        if (networkError) throw new HttpRequestException();
        return geocodeOverride.Length > 0 ? geocodeOverride : JsonSerializer.Serialize(new
        {
            candidates = new[] { new { address = "1 Church Street", score = 1, attributes = new { Addr_type = type }, location = new { x = 18.729966980745, y = -33.462424796756 } } }
        });
    });
    var boundary = new Handler(async request =>
    {
        var body = await request.Content!.ReadAsStringAsync();
        Check(body.Contains("18.729966980745") && body.Contains("-33.462424796756") && body.Contains("returnGeometry=false") && !body.Contains("token"), "Boundary uses returned coordinates anonymously without geometry");
        return JsonSerializer.Serialize(new { features = names.Select(name => new { attributes = new Dictionary<string, string>
        {
            ["AFRIGIS_LocalMunicipalities.S12_NAME"] = name,
            ["AFRIGIS_LocalMunicipalities.MUN_CODE"] = "test-code"
        } }) });
    });
    using var gc = new HttpClient(geocode);
    using var bc = new HttpClient(boundary);
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ArcGIS:ApiKey"] = configured ? "test-only-placeholder" : null }).Build();
    var service = new MunicipalRoutingService(new(gc, config), new(bc), NullLogger<MunicipalRoutingService>.Instance);
    var result = await service.ResolveAsync(address);
    if (result.Failure is RoutingFailure.AddressNotPrecise or RoutingFailure.Unavailable)
        Check(boundary.Calls == 0, "Rejected geocode never reaches boundary service");
    return result;
}
foreach (var municipality in LicenceApplicationCatalog.Municipalities)
{
    var shortName = municipality[..^" Municipality".Length];
    Check(LicenceApplicationCatalog.MapMunicipality(" " + shortName.ToLowerInvariant() + " ") == municipality, "Explicit short alias: " + shortName);
    var result = await Run("PointAddress", [shortName + " Local Municipality"]);
    Check(result.Success && result.Municipality == municipality, "Canonical mapping: " + municipality);
}
foreach (var type in new[] { "Subaddress", "StreetAddress" })
    Check((await Run(type, ["Swartland Municipality"])).Success, "Precise type accepted without score threshold: " + type);
foreach (var type in new[] { "Postal", "Locality", "StreetName", "StreetAddressExt", "POI", "Unknown" })
    Check((await Run(type, [])).Failure == RoutingFailure.AddressNotPrecise, "Coarse type rejected: " + type);
Check((await Run("PointAddress", [], "{\"candidates\":[]}")).Failure == RoutingFailure.AddressNotPrecise, "No geocode rejected");
Check((await Run("PointAddress", [])).Failure == RoutingFailure.NoMunicipality, "No boundary rejected");
Check((await Run("PointAddress", ["Swartland Municipality", "Bergrivier Municipality"])).Failure == RoutingFailure.MultipleMunicipalities, "Multiple boundaries rejected");
Check((await Run("PointAddress", ["City of Cape Town Metropolitan Municipality"])).Failure == RoutingFailure.Unsupported, "Cape Town rejected");
Check((await Run("PointAddress", [], configured: false)).Failure == RoutingFailure.Unavailable, "Missing key blocks routing");
Check((await Run("PointAddress", [], networkError: true)).Failure == RoutingFailure.Unavailable, "Network failure blocks routing");
Check((await Run("PointAddress", [], "not json")).Failure == RoutingFailure.Unavailable, "Malformed response blocks routing");
Check((await Run("PointAddress", [], "{\"candidates\":[{\"address\":\"Test\",\"attributes\":{\"Addr_type\":\"PointAddress\"},\"location\":{\"x\":18,\"y\":91}}]}")).Failure == RoutingFailure.AddressNotPrecise, "Out-of-range coordinates rejected");
Check((await Run("PointAddress", [], "{\"error\":{\"code\":498}}")).Failure == RoutingFailure.Unavailable, "API error in HTTP success response blocks routing");
Console.WriteLine("All routing checks passed; no real APIs or database used.");
await PoiTests.RunAsync();

sealed class Handler(Func<HttpRequestMessage, Task<string>> respond) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        return new(HttpStatusCode.OK) { Content = new StringContent(await respond(request)) };
    }
}
