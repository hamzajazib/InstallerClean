using System.Globalization;
using InstallerClean.Helpers;
using InstallerClean.Models;
using InstallerClean.Resources;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The line naming the drives and shares a scan, or the check made before a Move or
/// Delete, carried on without. Every screen that names them takes the line from here, so
/// the words and the way each root is named are pinned once for all of them.
/// </summary>
public class SourcesGivenUpReportTests
{
    private const string Tail =
        " and left alone any file still to be checked against it. Once it's responding normally, Re-scan.";

    private static SourceRootGivenUp Root(string root) => new(root, SourceRootGiveUpRoute.NoAnswer, 1);

    [Fact]
    public void Nothing_given_up_produces_no_line()
    {
        // Each host tests the line for emptiness to decide whether it appears at all,
        // and a pass that gave nothing up is the commonest pass by far.
        using var scope = new LocalisationScope(British);

        Assert.Equal(string.Empty, SourcesGivenUpReport.WindowLine([]));
    }

    [Theory]
    [InlineData("D:", "InstallerClean carried on without drive D:" + Tail)]
    [InlineData("d:", "InstallerClean carried on without drive D:" + Tail)]
    [InlineData(@"\\nas\installers", @"InstallerClean carried on without \\nas\installers" + Tail)]
    [InlineData(@"GLOBALROOT\Device\Mup\nas\installers", @"InstallerClean carried on without GLOBALROOT\Device\Mup\nas\installers" + Tail)]
    public void One_drive_or_share_is_named_inside_the_sentence(string root, string line)
    {
        using var scope = new LocalisationScope(British);

        Assert.Equal(line, SourcesGivenUpReport.WindowLine([Root(root)]));
    }

    [Fact]
    public void A_drive_takes_the_drive_form_and_anything_else_the_path_form()
    {
        // Against the keys rather than the English, so a language whose two forms differ
        // in more than the word for "drive" is held to the right one.
        using var scope = new LocalisationScope(British);

        Assert.Equal(string.Format(Strings.Summary_SourceGivenUp_Drive, "E:"),
            SourcesGivenUpReport.WindowLine([Root("e:")]));
        Assert.Equal(string.Format(Strings.Summary_SourceGivenUp_Path, @"\\nas\installers"),
            SourcesGivenUpReport.WindowLine([Root(@"\\nas\installers")]));
    }

    [Fact]
    public void More_than_one_are_listed_in_brackets_in_the_order_given_up()
    {
        using var scope = new LocalisationScope(British);

        Assert.Equal(
            @"InstallerClean carried on without more than one drive or share (drive D:, \\nas\installers, drive E:) "
            + "and left alone any file still to be checked against one of them. "
            + "Once they're responding normally, Re-scan.",
            SourcesGivenUpReport.WindowLine([Root("d:"), Root(@"\\nas\installers"), Root("E:")]));
    }

    [Fact]
    public void Each_item_in_the_list_is_joined_with_the_language_s_own_separator()
    {
        using var scope = new LocalisationScope(British);

        Assert.Equal(
            string.Format(Strings.Summary_SourcesGivenUp,
                string.Format(Strings.Display_DriveName, "D:") + Strings.Display_ListSeparator + @"\\nas\installers"),
            SourcesGivenUpReport.WindowLine([Root("D:"), Root(@"\\nas\installers")]));
    }

    [Fact]
    public void A_drive_letter_in_the_list_takes_its_capital_under_Turkish_too()
    {
        var turkish = CultureInfo.GetCultureInfo("tr-TR");
        using var scope = new LocalisationScope(turkish);
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = turkish;
        try
        {
            Assert.Equal(string.Format(Strings.Summary_SourceGivenUp_Drive, "I:"),
                SourcesGivenUpReport.WindowLine([Root("i:")]));
            Assert.Contains(string.Format(Strings.Display_DriveName, "I:"),
                SourcesGivenUpReport.WindowLine([Root("i:"), Root("j:")]), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");

    /// <summary>
    /// Pins the app's language for one test and drops it on the way out. The pin is
    /// process-global, which the assembly's serial test run (see AssemblyInfo.cs) is what
    /// makes safe.
    /// </summary>
    private sealed class LocalisationScope : IDisposable
    {
        public LocalisationScope(CultureInfo culture) => Localisation.Set(culture, culture);

        public void Dispose() => Localisation.Reset();
    }
}
