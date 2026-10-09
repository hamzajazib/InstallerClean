using System.Globalization;
using InstallerClean.Helpers;
using InstallerClean.Resources;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// Covers Localisation's override: the language the resx strings resolve in and
/// the culture DisplayHelpers formats numbers, times and dates against, each
/// pinned above the thread's culture. A language picked in the app pins the
/// first to the pick and the second to the PC's regional format
/// (<see cref="Localisation.SetPickedLanguage"/>); the other tests here pin the
/// two by hand. Covering it at all depends on LocalisationScope: an override
/// that can be set but not unset leaks the pinned language into every test that
/// runs after it, rewriting their expected strings.
///
/// The assertions deliberately hold no French text. French punctuation needs
/// narrow no-break spaces, which many editors and text tools normalise away
/// without showing it, so an expected value typed into C# here could differ
/// from the resx by an invisible character. Every expectation below is read
/// back out of the resx, or is a decimal separator.
/// </summary>
public class LocalisationOverrideTests
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr");
    private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");
    private static readonly CultureInfo UnitedStates = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Germany = CultureInfo.GetCultureInfo("de-DE");

    [Fact]
    public void An_explicit_pick_drives_the_generated_string_accessor()
    {
        var english = Strings.ResourceManager.GetString("Completion.AllClean", British);
        var french = Strings.ResourceManager.GetString("Completion.AllClean", French);
        Assert.NotEqual(english, french); // guards the test itself: the satellite must differ

        using var scope = new LocalisationScope(French, French);

        Assert.Equal(french, Strings.Completion_AllClean);
    }

    [Fact]
    public void A_pinned_format_culture_drives_number_formatting_even_when_the_thread_disagrees()
    {
        // The thread stays British while the format culture is pinned to French.
        // This is the case Localisation exists for: a thread culture does not
        // reliably survive the dispatcher's per-callback context, so the override
        // is what every window has to read.
        using var thread = new CultureScope(British);
        using var scope = new LocalisationScope(French, French);

        var size = DisplayHelpers.FormatSize(1024);

        Assert.Contains("1,0", size);
        Assert.DoesNotContain("1.0", size);
    }

    [Fact]
    public void The_long_elapsed_form_follows_the_format_culture_too()
    {
        // Every formatter in DisplayHelpers has to pass
        // Localisation.FormatCulture explicitly. One that does not renders the
        // French sentence with an English decimal point, which is what this
        // catches: the number is the only part of the line that gives it away.
        using var thread = new CultureScope(British);
        using var scope = new LocalisationScope(French, French);

        var elapsed = DisplayHelpers.FormatElapsedLong(TimeSpan.FromSeconds(1.5));

        Assert.Contains("1,5", elapsed);
        Assert.DoesNotContain("1.5", elapsed);
    }

    [Fact]
    public void An_explicit_pick_drives_the_plural_rules()
    {
        // English pluralises zero ("0 files"), French does not ("0 fichier").
        // Same count, same call: only the pinned language decides.
        Assert.Equal(DisplayHelpers.PluraliseFile(2), DisplayHelpers.PluraliseFile(0));

        using var scope = new LocalisationScope(French, French);

        Assert.NotEqual(DisplayHelpers.PluraliseFile(2), DisplayHelpers.PluraliseFile(0));
        Assert.Equal(Strings.ResourceManager.GetString("Plural.File.Singular", French),
            DisplayHelpers.PluraliseFile(0));
    }

    // ---- A language picked in the app ----

    // 16:40 UTC, read in UTC, so every time below is 16:40 or 4:40 PM.
    private static readonly DateTime SixteenForty = new(2030, 6, 16, 16, 40, 0, DateTimeKind.Utc);

    // 1,023.9 KB: a figure with both a group and a decimal separator in it.
    private const long JustUnderAMegabyte = 1_048_474;

    [Fact]
    public void English_picked_on_a_US_regional_format_keeps_US_times_and_dates()
    {
        using var thread = new CultureScope(UnitedStates);
        using var pick = new PickedLanguageScope(British);

        var (time, date) = DisplayHelpers.FormatTimeAndDate(SixteenForty, TimeZoneInfo.Utc);

        Assert.Equal(British, Localisation.UiCulture);
        Assert.Same(UnitedStates, Localisation.FormatCulture);
        Assert.Contains("4:40", time);
        Assert.Contains("PM", time);
        Assert.Equal("June 16", date);
        Assert.StartsWith("1,023.9", DisplayHelpers.FormatSize(JustUnderAMegabyte));
    }

    [Fact]
    public void A_pick_keeps_the_regional_format_as_the_user_has_customised_it()
    {
        // A US format switched to a 24-hour clock in Windows: the culture the
        // thread holds carries the change, and a culture looked up by name does not.
        var customised = (CultureInfo)UnitedStates.Clone();
        customised.DateTimeFormat.ShortTimePattern = "HH:mm";
        using var thread = new CultureScope(customised);
        using var pick = new PickedLanguageScope(British);

        var (time, date) = DisplayHelpers.FormatTimeAndDate(SixteenForty, TimeZoneInfo.Utc);

        Assert.Equal("16:40", time);
        Assert.Equal("June 16", date);
    }

    [Fact]
    public void German_picked_on_a_US_regional_format_writes_US_numbers_and_German_dates()
    {
        using var thread = new CultureScope(UnitedStates);
        using var pick = new PickedLanguageScope(German);

        var (time, date) = DisplayHelpers.FormatTimeAndDate(SixteenForty, TimeZoneInfo.Utc);

        Assert.Equal("16:40", time);
        Assert.Equal("16. Juni", date);
        Assert.StartsWith("1,023.9", DisplayHelpers.FormatSize(JustUnderAMegabyte));
    }

    [Fact]
    public void English_picked_on_a_German_regional_format_writes_German_numbers_and_English_dates()
    {
        using var thread = new CultureScope(Germany);
        using var pick = new PickedLanguageScope(British);

        var (time, date) = DisplayHelpers.FormatTimeAndDate(SixteenForty, TimeZoneInfo.Utc);

        Assert.Equal("16:40", time);
        Assert.Equal("16 June", date);
        Assert.StartsWith("1.023,9", DisplayHelpers.FormatSize(JustUnderAMegabyte));
        Assert.StartsWith("1,5", DisplayHelpers.FormatElapsedLong(TimeSpan.FromSeconds(1.5)));
    }

    [Fact]
    public void Automatic_falls_back_to_the_ambient_thread_culture()
    {
        // No override set: the Automatic case, and the whole of the CLI.
        Assert.Null(Localisation.UiCultureOverride);
        using var thread = new CultureScope(French);

        Assert.Equal(French, Localisation.FormatCulture);
    }

    [Fact]
    public void Dropping_the_override_restores_the_fallback()
    {
        var english = Strings.Completion_AllClean;

        using (new LocalisationScope(French, French))
        {
            Assert.NotEqual(english, Strings.Completion_AllClean);
        }

        Assert.Null(Localisation.UiCultureOverride);
        Assert.Equal(english, Strings.Completion_AllClean);
    }

    /// <summary>
    /// Pins the app's language for one test and drops it again on the way out.
    /// The override is process-global, so a test that leaked it would rewrite
    /// every string the tests after it expect; the assembly runs its tests
    /// serially (see AssemblyInfo.cs) so a pin is never visible to a test
    /// running alongside.
    /// </summary>
    private sealed class LocalisationScope : IDisposable
    {
        public LocalisationScope(CultureInfo uiCulture, CultureInfo formatCulture) =>
            Localisation.Set(uiCulture, formatCulture);

        public void Dispose() => Localisation.Reset();
    }

    /// <summary>
    /// Picks a language the way the app does at startup, reading the regional
    /// format off the thread, and drops the pin on the way out.
    /// </summary>
    private sealed class PickedLanguageScope : IDisposable
    {
        public PickedLanguageScope(CultureInfo language) =>
            Localisation.SetPickedLanguage(language);

        public void Dispose() => Localisation.Reset();
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous;

        public CultureScope(CultureInfo culture)
        {
            _previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = culture;
        }

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
