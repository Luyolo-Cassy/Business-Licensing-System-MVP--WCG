using PdfSharp.Fonts;

namespace BusinessLicensing_Practice.Fonts;

public sealed class LiberationSansFontResolver : IFontResolver
{
    public const string FamilyName = "Liberation Sans";

    private const string RegularFace = "LiberationSans-Regular";
    private const string BoldFace = "LiberationSans-Bold";

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        if (!string.Equals(familyName, FamilyName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new FontResolverInfo(bold ? BoldFace : RegularFace, false, italic);
    }

    public byte[]? GetFont(string faceName)
    {
        var fileName = faceName switch
        {
            RegularFace => "LiberationSans-Regular.ttf",
            BoldFace => "LiberationSans-Bold.ttf",
            _ => null
        };

        return fileName == null
            ? null
            : File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", fileName));
    }
}
