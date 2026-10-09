using System.Globalization;

namespace InstallerClean.Helpers;

/// <summary>
/// The culture the app resolves resx strings against, and the one it formats
/// numbers, times and dates against.
/// </summary>
/// <remarks>
/// The resx lookups (the XAML <c>Translate</c> extension and the generated
/// <c>Strings</c> accessor) and <c>DisplayHelpers</c>'s size, count, elapsed and
/// time-and-date formatting read <see cref="UiCulture"/>/<see cref="FormatCulture"/>
/// rather than <see cref="CultureInfo.CurrentUICulture"/>/<see cref="CultureInfo.CurrentCulture"/>
/// directly. An explicit override here is honoured by every thread and window. A
/// culture assigned to the current thread is not: it does not reliably persist
/// across the WPF dispatcher's per-callback execution context, so it reaches a
/// window built during startup and not one opened later from a click.
/// When no override is set, both fall back to the ambient thread culture: the
/// Automatic case (follow the OS language), and the whole of the CLI, which
/// never sets an override and localises through exactly this fallback.
/// </remarks>
public static class Localisation
{
    public static CultureInfo? UiCultureOverride { get; private set; }
    public static CultureInfo? FormatCultureOverride { get; private set; }

    public static CultureInfo UiCulture => UiCultureOverride ?? CultureInfo.CurrentUICulture;
    public static CultureInfo FormatCulture => FormatCultureOverride ?? CultureInfo.CurrentCulture;

    /// <summary>Pins the resx and formatting cultures for the rest of the process.</summary>
    public static void Set(CultureInfo uiCulture, CultureInfo formatCulture)
    {
        UiCultureOverride = uiCulture;
        FormatCultureOverride = formatCulture;
    }

    /// <summary>
    /// Pins <paramref name="language"/>, a language picked in the app, for the
    /// resx strings, and pins the format culture to the PC's regional format: the
    /// thread's own culture as this is called, which carries the user's Windows
    /// customisations (a culture looked up by name does not). Numbers therefore
    /// keep the regional format whatever language is picked, as they do on
    /// Automatic. Times and dates take the regional format only where it is a
    /// culture of the picked language (<see cref="DisplayHelpers.FormatTimeAndDate"/>).
    /// Call it before anything sets the thread's culture.
    /// </summary>
    public static void SetPickedLanguage(CultureInfo language) =>
        Set(language, CultureInfo.CurrentCulture);

    /// <summary>
    /// Drops both overrides, so the cultures fall back to the ambient thread
    /// culture again. The app never needs this: it pins the language once at
    /// startup and lives with it. Tests do, and without a way back nothing
    /// could call <see cref="Set"/> at all, because the override is process-
    /// global and every <c>Strings</c> lookup reads it, so one test pinning
    /// French would rewrite the expected strings of every test that ran after
    /// it.
    /// </summary>
    internal static void Reset()
    {
        UiCultureOverride = null;
        FormatCultureOverride = null;
    }
}
