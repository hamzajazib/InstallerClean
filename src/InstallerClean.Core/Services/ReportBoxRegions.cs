namespace InstallerClean.Services;

/// <summary>
/// Where the "Send anonymous report" box on the PC's first finished card starts
/// unticked: the 27 member states of the European Union; Iceland, Liechtenstein and
/// Norway; and the parts of the European Union that ISO 3166-1 gives a code of their own
/// (French Guiana, Guadeloupe, Martinique, Reunion, Mayotte, Saint Martin and the Aland
/// Islands). Everywhere else it starts ticked.
/// </summary>
public static class ReportBoxRegions
{
    /// <summary>The ISO 3166-1 two-letter codes of the regions named above.</summary>
    public static readonly IReadOnlySet<string> StartUnticked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // The European Union.
        "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "HU", "IE",
        "IT", "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE",
        // The rest of the European Economic Area.
        "IS", "LI", "NO",
        // Parts of the European Union with codes of their own.
        "GF", "GP", "MQ", "RE", "YT", "MF", "AX",
    };

    /// <summary>
    /// Whether the box starts ticked for <paramref name="region"/>, a value
    /// <see cref="IWindowsRegion.Read"/> returned. Only a two-letter code outside
    /// <see cref="StartUnticked"/> starts it ticked: a region in the list, a number, an
    /// empty value and a region that could not be read all start it unticked.
    /// </summary>
    public static bool StartsTicked(string? region) =>
        region is { Length: 2 }
        && char.IsAsciiLetter(region[0]) && char.IsAsciiLetter(region[1])
        && !StartUnticked.Contains(region);
}
