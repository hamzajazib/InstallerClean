using System.Globalization;

namespace InstallerClean.Helpers;

/// <summary>
/// Builds a machine-read line in English whatever the display language: the
/// Application-log entries, which RMM tooling matches on exact English phrases.
/// </summary>
/// <remarks>
/// The mechanism is a thread-culture swap, which reaches the resx template, a pluralised
/// noun and a size together only while <see cref="Localisation"/> has no override set:
/// <see cref="Localisation.UiCulture"/> and <see cref="Localisation.FormatCulture"/> fall
/// through to the thread's cultures only then. A host that calls
/// <see cref="Localisation.Set"/> or <see cref="Localisation.SetPickedLanguage"/> before a
/// machine line is built gets that line in the language it pinned, so a line built here
/// is built before either is called.
/// </remarks>
internal static class MachineCulture
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    /// <summary>
    /// Runs <paramref name="build"/> with the thread UI and format cultures forced to
    /// en-GB and restores both before returning. The restore is in a finally so a throw
    /// mid-build cannot leak en-GB onto the thread; the build is synchronous, so the swap
    /// never spans an await.
    /// </summary>
    internal static string English(Func<string> build)
    {
        var ui = CultureInfo.CurrentUICulture;
        var format = CultureInfo.CurrentCulture;
        CultureInfo.CurrentUICulture = EnGb;
        CultureInfo.CurrentCulture = EnGb;
        try
        {
            return build();
        }
        finally
        {
            CultureInfo.CurrentUICulture = ui;
            CultureInfo.CurrentCulture = format;
        }
    }
}
