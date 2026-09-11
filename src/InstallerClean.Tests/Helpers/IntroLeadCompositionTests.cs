using System.Globalization;
using InstallerClean.Helpers;
using InstallerClean.Resources;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The main window's first line, across all sixteen languages.
///
/// MainViewModel.IntroLead picks one of four strings and the window binds it as
/// the TextBlock's text. None of the four carries a square bracket, in any
/// language.
///
/// That is visible to no existing gate. A bracket is an ordinary character in a
/// resx value, so a translator can add one and check-resx-parity, which reads
/// key presence and placeholder arity, is looking at something else. Nothing
/// splits these values, so a bracket in one paints on screen, at the top of the
/// window and in the sentence a reader meets first.
///
/// LinkPhraseCompositionTests holds the opposite rule over the sentences that
/// do carry a link phrase.
/// </summary>
public class IntroLeadCompositionTests
{
    /// <summary>
    /// The four leads, each of which renders as plain text. Keys rather than
    /// typed accessors, because the assertion is about what each language ships
    /// and the lookup has to name a culture.
    /// </summary>
    private static readonly string[] LeadKeys =
    {
        "Body.MainExplanation.Lead", // a scan found files
        "Error.ScanFailedTitle",     // a scan failed, startup or Re-scan
        "Body.NotScanned.Lead",      // the startup scan was cancelled
        "Body.PendingReboot.Lead",   // files found, but Windows Installer is busy
    };

    public static TheoryData<string> Cultures()
    {
        var data = new TheoryData<string>();
        foreach (var name in SupportedLanguages.CultureNames) data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Every_lead_renders_as_plain_text(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var faults = new List<string>();

        foreach (var key in LeadKeys)
        {
            var value = Lead(key, culture);

            // Either bracket on its own is the whole fault: the character is
            // drawn as it stands. The key is named in the message because four
            // leads are read and a fault quoting only the value would leave a
            // reader working out which state it came from.
            if (value.Contains('[') || value.Contains(']'))
                faults.Add($"{key} carries a square bracket: \"{value}\"");
        }

        // Collected across the four and asserted once, so one run names every
        // lead a language gets wrong rather than stopping at the first.
        Assert.True(faults.Count == 0, $"{cultureName}: {string.Join("; ", faults)}");
    }

    /// <summary>
    /// Resolves a lead the way the window does: through the app's UI culture,
    /// so the installer-folder token is spent before anything counts brackets.
    /// The token's replacement is a real path and could in principle contain
    /// one, which is the reason for going through this door rather than reading
    /// the raw resource.
    /// </summary>
    private static string Lead(string key, CultureInfo culture)
    {
        using var scope = new LocalisationScope(culture);
        return Strings.Get(key);
    }

    private sealed class LocalisationScope : IDisposable
    {
        public LocalisationScope(CultureInfo culture) => Localisation.Set(culture, culture);

        public void Dispose() => Localisation.Reset();
    }
}
