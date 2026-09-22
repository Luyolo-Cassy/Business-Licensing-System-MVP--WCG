using System.Net;
using System.Text.Json;
using BusinessLicensing_Practice.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

internal static class PoiTests
{
    public static async Task RunAsync()
    {
        var input = new TradingAddress("Civic Centre, Van den Berg Street", "", "Riversdale", "Riversdale", "6670");
        Dictionary<string, object> Candidate(string type = "POI", string? change = null, string value = "", double x = 21.25907)
        {
            var a = new Dictionary<string, string> { ["Addr_type"] = type, ["PlaceName"] = "Riversdale Civic Centre",
                ["StAddr"] = "50 Van den Berg Street", ["AddNum"] = "50", ["StName"] = "Van den Berg", ["StType"] = "Street",
                ["City"] = "Hessequa", ["District"] = "Riversdale", ["Postal"] = "6670", ["Region"] = "Western Cape", ["Country"] = "ZAF" };
            if (change is not null) a[change] = value;
            return new() { ["address"] = "Riversdale Civic Centre", ["attributes"] = a,
                ["score"] = 1, ["location"] = new { x, y = -34.09196 } };
        }
        async Task Test(string label, object[] candidates, RoutingFailure expected = RoutingFailure.None,
            TradingAddress? address = null, string[]? boundaries = null, string primaryType = "StreetName")
        {
            int requests = 0, boundaryRequests = 0;
            var errors = new List<string>();
            using var gc = new HttpClient(new Stub(async request =>
            {
                requests++;
                var body = await request.Content!.ReadAsStringAsync();
                if (requests == 1)
                {
                    if (body.Contains("SingleLine")) errors.Add("Precise path replaced");
                    return JsonSerializer.Serialize(new { candidates = new[] { Candidate(primaryType) } });
                }
                if (!body.Contains("SingleLine=") || !body.Contains("maxLocations=50") || !body.Contains("PlaceName")) errors.Add("Fallback attributes/candidates missing");
                return JsonSerializer.Serialize(new { candidates });
            }));
            using var bc = new HttpClient(new Stub(async request =>
            {
                boundaryRequests++;
                var body = await request.Content!.ReadAsStringAsync();
                if (!body.Contains("21.25907") || !body.Contains("-34.09196") || body.Contains("token=")) errors.Add("Boundary coordinate or secret isolation failed");
                return JsonSerializer.Serialize(new { features = (boundaries ?? ["Hessequa Local Municipality"]).Select(name => new
                {
                    attributes = new Dictionary<string, string>
                    {
                        ["AFRIGIS_LocalMunicipalities.S12_NAME"] = name,
                        ["AFRIGIS_LocalMunicipalities.MUN_CODE"] = name switch
                        {
                            "Hessequa Local Municipality" => "WC042",
                            "Swartland Local Municipality" => "WC015",
                            _ => "unknown-code"
                        }
                    }
                }) });
            }));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ArcGIS:ApiKey"] = "test-only-placeholder" }).Build();
            var service = new MunicipalRoutingService(new(gc, config), new(bc), NullLogger<MunicipalRoutingService>.Instance);
            var result = await service.ResolveAsync(address ?? input);
            if (result.Failure != expected || errors.Count > 0 || requests != (primaryType == "PointAddress" ? 1 : 2)
                || (expected == RoutingFailure.AddressNotPrecise && boundaryRequests != 0)
                || (expected == RoutingFailure.None && result.Municipality != "Hessequa Municipality"))
                throw new Exception(label + ": unexpected outcome " + result.Failure + " " + string.Join(",", errors));
            Console.WriteLine("PASS: " + label);
        }
        await Test("Precise result bypasses fallback", [], primaryType: "PointAddress");
        await Test("Riversdale Civic Centre validated POI succeeds without score threshold", [Candidate()]);
        await Test("Named numbered premises succeeds", [Candidate()], address: input with { Line1 = "Riversdale Civic Centre, 50 Van den Berg Street" });
        await Test("Premises in Line1 and street in Line2 succeeds", [Candidate()], address: input with { Line1 = "Civic Centre", Line2 = "Van den Berg Street" });
        foreach (var (field, value) in new[] { ("PlaceName", "Other Centre"), ("StAddr", "50 Other Street"), ("District", "Other Town"),
                     ("Postal", "9999"), ("Region", "Eastern Cape"), ("Country", "USA"), ("PlaceName", ""), ("StAddr", "") })
            await Test("Reject conflicting/missing " + field + ": " + value, [Candidate(change: field, value: value)], RoutingFailure.AddressNotPrecise);
        await Test("Conflicting supplied number rejected", [Candidate()], RoutingFailure.AddressNotPrecise, input with { Line1 = "Civic Centre, 51 Van den Berg Street" });
        await Test("Unrelated Urcsa Piketberg rejected", [Candidate(change: "PlaceName", value: "Urcsa Piketberg")], RoutingFailure.AddressNotPrecise);
        foreach (var type in new[] { "Postal", "StreetName", "StreetAddressExt", "Locality" })
            await Test("Fallback cannot accept " + type + " alone", [Candidate(type)], RoutingFailure.AddressNotPrecise);
        await Test("Invalid coordinates rejected", [Candidate(x: 181)], RoutingFailure.AddressNotPrecise);
        await Test("Missing provider postcode allowed when other components agree", [Candidate(change: "Postal", value: "")]);
        await Test("Equivalent POI and address are not ambiguous", [Candidate(), Candidate("PointAddress", x: 21.2591)]);
        await Test("Duplicate identical POIs accepted", [Candidate(), Candidate()]);
        await Test("Conflicting POI locations rejected", [Candidate(), Candidate(x: 21.3)], RoutingFailure.AddressNotPrecise);
        await Test("Remote address candidate rejected", [Candidate(), Candidate("PointAddress", x: 22)], RoutingFailure.AddressNotPrecise);
        await Test("Conflicting candidate street rejected", [Candidate(), Candidate("PointAddress", "StAddr", "50 Other Street")], RoutingFailure.AddressNotPrecise);
        await Test("Zero boundaries fails", [Candidate()], RoutingFailure.NoMunicipality, boundaries: []);
        await Test("Multiple boundaries fails", [Candidate()], RoutingFailure.MultipleMunicipalities, boundaries: ["Hessequa Local Municipality", "Swartland Local Municipality"]);
        await Test("Unsupported boundary keeps existing failure", [Candidate()], RoutingFailure.Unsupported, boundaries: ["City of Cape Town Metropolitan Municipality"]);
    }
    private sealed class Stub(Func<HttpRequestMessage, Task<string>> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            new(HttpStatusCode.OK) { Content = new StringContent(await respond(request)) };
    }
}
