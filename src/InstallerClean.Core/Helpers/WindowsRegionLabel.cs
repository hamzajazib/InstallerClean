using InstallerClean.Services;

namespace InstallerClean.Helpers;

/// <summary>
/// The Country or region set in Windows, as the opt-in report carries it.
/// </summary>
public static class WindowsRegionLabel
{
    /// <summary>What <see cref="For"/> answers where nothing could be read.</summary>
    public const string Unreadable = "unreadable";

    /// <summary>What <see cref="For"/> answers for a value of neither shape Windows documents.</summary>
    public const string Unrecognised = "unrecognised";

    /// <summary>
    /// <paramref name="region"/>, a value <see cref="IWindowsRegion.Read"/> returned, in
    /// one of the shapes the receiver accepts for this field: two ASCII letters, upper-cased,
    /// for an ISO 3166-1 code; three ASCII digits, as read, for a UN M.49 number;
    /// <see cref="Unreadable"/> for null or empty; and <see cref="Unrecognised"/> for
    /// anything else. Widening a shape here needs the receiver's pattern widened first.
    /// </summary>
    public static string For(string? region)
    {
        if (string.IsNullOrEmpty(region)) return Unreadable;

        if (region.Length == 2 && region.All(char.IsAsciiLetter))
            return region.ToUpperInvariant();

        if (region.Length == 3 && region.All(char.IsAsciiDigit))
            return region;

        return Unrecognised;
    }
}
