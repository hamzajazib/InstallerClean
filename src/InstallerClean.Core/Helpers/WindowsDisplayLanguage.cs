using System.Globalization;
using InstallerClean.Interop.Native;

namespace InstallerClean.Helpers;

/// <summary>
/// The language Windows shows its own interface in for this user, as a language with
/// no country, for the opt-in report.
/// </summary>
public static class WindowsDisplayLanguage
{
    /// <summary>What <see cref="Current"/> answers where Windows gave no display language.</summary>
    public const string Unreadable = "unreadable";

    /// <summary>
    /// What <see cref="Neutral"/> answers for a name that is not a culture, or that is
    /// not a language and at most a script once the country is off.
    /// </summary>
    public const string Unrecognised = "unrecognised";

    /// <summary>
    /// The user's display language, reduced by <see cref="Neutral"/>. Read from Windows
    /// rather than from <see cref="CultureInfo.CurrentUICulture"/>, which a language
    /// picked in the app replaces.
    /// </summary>
    public static string Current() => Neutral(Read());

    /// <summary>
    /// The first of the user's preferred display languages, or null where Windows gave
    /// none or the call is not there to make.
    /// </summary>
    private static string? Read()
    {
        try
        {
            uint size = 0;
            if (!Kernel32.GetUserPreferredUILanguages(Kernel32.MUI_LANGUAGE_NAME, out _, null, ref size)
                || size == 0)
                return null;

            var buffer = new char[size];
            if (!Kernel32.GetUserPreferredUILanguages(Kernel32.MUI_LANGUAGE_NAME, out _, buffer, ref size))
                return null;

            // The list is one name per null-ended entry; the first is the display language.
            var end = Array.IndexOf(buffer, '\0');
            return end > 0 ? new string(buffer, 0, end) : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// The language of <paramref name="name"/> with any country or region taken off and
    /// a script kept: <c>en-US</c> gives <c>en</c>, <c>zh-TW</c> gives <c>zh-Hant</c> and
    /// <c>sr-Latn-RS</c> gives <c>sr-Latn</c>. It walks the culture's parents to the
    /// first neutral culture, which names a language and, for a language written more
    /// than one way, its script.
    ///
    /// ANYTHING NOT THEN A LANGUAGE OF TWO OR THREE LOWER-CASE LETTERS, WITH AT MOST A
    /// FOUR-LETTER SCRIPT, IS <see cref="Unrecognised"/>. The report then carries nothing
    /// that names a country, and nothing outside the shape the receiver accepts for this
    /// field. Widening the pattern here needs the receiver's pattern widened first.
    /// </summary>
    public static string Neutral(string? name)
    {
        if (string.IsNullOrEmpty(name)) return Unreadable;

        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return Unrecognised;
        }

        while (!culture.IsNeutralCulture && culture.Parent.Name.Length > 0)
            culture = culture.Parent;

        return IsLanguageAndScript(culture.Name) ? culture.Name : Unrecognised;
    }

    /// <summary>
    /// Two or three lower-case ASCII letters, optionally followed by a hyphen and a
    /// script: one upper-case and three lower-case ASCII letters.
    /// </summary>
    private static bool IsLanguageAndScript(string name)
    {
        var parts = name.Split('-');
        if (parts.Length > 2
            || parts[0].Length is < 2 or > 3
            || !parts[0].All(char.IsAsciiLetterLower))
            return false;

        return parts.Length == 1
            || (parts[1].Length == 4
                && char.IsAsciiLetterUpper(parts[1][0])
                && parts[1].Skip(1).All(char.IsAsciiLetterLower));
    }
}
