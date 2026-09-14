namespace BusinessLicensing_Practice.Services;

public sealed class MunicipalRoutingService(ArcGisGeocodingService geocoder, WcgMunicipalBoundaryService boundaries,
    ILogger<MunicipalRoutingService> logger)
{
    public async Task<MunicipalRoutingResult> ResolveAsync(TradingAddress address, CancellationToken cancellationToken = default)
    {
        try
        {
            var point = await geocoder.GeocodeAsync(address, cancellationToken);
            if (point is null) return new(null, RoutingFailure.AddressNotPrecise);
            var matches = await boundaries.FindMunicipalitiesAsync(point, cancellationToken);
            if (matches.Count == 0) return new(null, RoutingFailure.NoMunicipality);
            if (matches.Count != 1) return new(null, RoutingFailure.MultipleMunicipalities);
            var canonical = LicenceApplicationCatalog.MapMunicipality(matches[0].Name);
            return canonical is null ? new(null, RoutingFailure.Unsupported, matches[0].Name)
                : new(canonical, RoutingFailure.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Never log request bodies, addresses, API responses, or exception details.
            logger.LogWarning("Municipal routing could not complete an external service request.");
            return new(null, RoutingFailure.Unavailable);
        }
    }
}
