using System.Collections.Immutable;

namespace BusinessLicensing_Practice.Services;

public sealed record WesternCapeMunicipalityDefinition(
    string RoutingName,
    string DefaultName,
    string WcgCode,
    string WcgBoundaryName);

public static class WesternCapeMunicipalityCatalog
{
    public static ImmutableArray<WesternCapeMunicipalityDefinition> Municipalities { get; } =
    [
        new("City of Cape Town Metropolitan Municipality", "City of Cape Town Metropolitan Municipality", "CPT", "City of Cape Town Metropolitan Municipality"),
        new("Matzikama Municipality", "Matzikama Municipality", "WC011", "Matzikama Local Municipality"),
        new("Cederberg Municipality", "Cederberg Municipality", "WC012", "Cederberg Local Municipality"),
        new("Bergrivier Municipality", "Bergrivier Municipality", "WC013", "Bergrivier Local Municipality"),
        new("Saldanha Bay Municipality", "Saldanha Bay Municipality", "WC014", "Saldanha Bay Local Municipality"),
        new("Swartland Municipality", "Swartland Municipality", "WC015", "Swartland Local Municipality"),
        new("Witzenberg Municipality", "Witzenberg Municipality", "WC022", "Witzenberg Local Municipality"),
        new("Drakenstein Municipality", "Drakenstein Municipality", "WC023", "Drakenstein Local Municipality"),
        new("Stellenbosch Municipality", "Stellenbosch Municipality", "WC024", "Stellenbosch Local Municipality"),
        new("Breede Valley Municipality", "Breede Valley Municipality", "WC025", "Breede Valley Local Municipality"),
        new("Langeberg Municipality", "Langeberg Municipality", "WC026", "Langeberg Local Municipality"),
        new("Theewaterskloof Municipality", "Theewaterskloof Municipality", "WC031", "Theewaterskloof Local Municipality"),
        new("Overstrand Municipality", "Overstrand Municipality", "WC032", "Overstrand Local Municipality"),
        new("Cape Agulhas Municipality", "Cape Agulhas Municipality", "WC033", "Cape Agulhas Local Municipality"),
        new("Swellendam Municipality", "Swellendam Municipality", "WC034", "Swellendam Local Municipality"),
        new("Kannaland Municipality", "Kannaland Municipality", "WC041", "Kannaland Local Municipality"),
        new("Hessequa Municipality", "Hessequa Municipality", "WC042", "Hessequa Local Municipality"),
        new("Mossel Bay Municipality", "Mossel Bay Municipality", "WC043", "Mossel Bay Local Municipality"),
        new("George Municipality", "George Municipality", "WC044", "George Local Municipality"),
        new("Oudtshoorn Municipality", "Oudtshoorn Municipality", "WC045", "Oudtshoorn Local Municipality"),
        new("Bitou Municipality", "Bitou Municipality", "WC047", "Bitou Local Municipality"),
        new("Knysna Municipality", "Knysna Municipality", "WC048", "Knysna Local Municipality"),
        new("Laingsburg Municipality", "Laingsburg Municipality", "WC051", "Laingsburg Local Municipality"),
        new("Prince Albert Municipality", "Prince Albert Municipality", "WC052", "Prince Albert Local Municipality"),
        new("Beaufort West Municipality", "Beaufort West Municipality", "WC053", "Beaufort West Local Municipality")
    ];

    public static WesternCapeMunicipalityDefinition? FindByCode(string? code) =>
        Municipalities.FirstOrDefault(item => string.Equals(item.WcgCode, code?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static WesternCapeMunicipalityDefinition? FindByRoutingName(string? routingName) =>
        Municipalities.FirstOrDefault(item => string.Equals(item.RoutingName, routingName?.Trim(), StringComparison.Ordinal));

    public static WesternCapeMunicipalityDefinition? FindByReservedName(string? name) =>
        Municipalities.FirstOrDefault(item => string.Equals(item.DefaultName, name?.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.WcgBoundaryName, name?.Trim(), StringComparison.OrdinalIgnoreCase));
}
