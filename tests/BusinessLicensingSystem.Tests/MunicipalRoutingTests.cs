using System.Net;
using System.Text.Json;
using BusinessLicensing_Practice.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BusinessLicensingSystem.Tests.MunicipalRouting;

[Trait("Feature", "Municipal Routing")]
public sealed class MunicipalRoutingTests
{
    private static readonly TradingAddress Address = new("1 Church Street", "", "Central", "Malmesbury", "7300");

    [Fact]
    public void WesternCapeCatalogue_ContainsAllMunicipalities() =>
        Assert.Equal(25, WesternCapeMunicipalityCatalog.Municipalities.Length);

    [Fact]
    public async Task PreciseAddress_RoutesToCanonicalMunicipality()
    {
        var result = await Resolve("PointAddress", ["Swartland Local Municipality"], ["WC015"]);
        Assert.True(result.Success);
        Assert.Equal("Swartland Municipality", result.Municipality);
    }

    [Fact]
    public async Task CoarseAddress_IsRejectedAsNotPrecise()
    {
        var result = await Resolve("Locality", []);
        Assert.Equal(RoutingFailure.AddressNotPrecise, result.Failure);
    }

    [Fact]
    public async Task MultipleBoundaryMatches_AreRejected()
    {
        var result = await Resolve("PointAddress", ["Swartland Local Municipality", "Bergrivier Local Municipality"], ["WC015", "WC013"]);
        Assert.Equal(RoutingFailure.MultipleMunicipalities, result.Failure);
    }

    [Fact]
    public async Task MissingArcGisKey_ReturnsUnavailableWithoutNetworkCall()
    {
        var geocode = new StubHandler(_ => throw new InvalidOperationException("HTTP must not be called"));
        var service = CreateService(geocode, new StubHandler(_ => "{}"), configured: false);

        var result = await service.ResolveAsync(Address);

        Assert.Equal(RoutingFailure.Unavailable, result.Failure);
        Assert.Equal(0, geocode.Calls);
    }

    private static async Task<MunicipalRoutingResult> Resolve(string type, string[] names, string[]? codes = null)
    {
        var geocode = new StubHandler(_ => JsonSerializer.Serialize(new
        {
            candidates = new[] { new { address = "1 Church Street", attributes = new { Addr_type = type }, location = new { x = 18.729966980745, y = -33.462424796756 } } }
        }));
        var boundary = new StubHandler(_ => JsonSerializer.Serialize(new
        {
            features = names.Select((name, index) => new { attributes = new Dictionary<string, string>
            {
                ["AFRIGIS_LocalMunicipalities.S12_NAME"] = name,
                ["AFRIGIS_LocalMunicipalities.MUN_CODE"] = codes?[index] ?? "unknown-code"
            } })
        }));
        return await CreateService(geocode, boundary).ResolveAsync(Address);
    }

    private static MunicipalRoutingService CreateService(StubHandler geocode, StubHandler boundary, bool configured = true)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ArcGIS:ApiKey"] = configured ? "offline-test-key" : null
        }).Build();
        return new(new ArcGisGeocodingService(new HttpClient(geocode), configuration),
            new WcgMunicipalBoundaryService(new HttpClient(boundary)), NullLogger<MunicipalRoutingService>.Instance);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(request)) });
        }
    }
}
