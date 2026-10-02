using System.Globalization;
using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// A path found by its backslashes rather than matched: in the line saying what a
/// scan is waiting for, which names a share, a drive or a path wherever each
/// language's word order puts it, and in the folder a Move sends files to, a path
/// on its own. A share's leading pair is joined, and each backslash inside the path
/// takes a break opportunity after it.
///
/// Pure string work. Where WPF's line breaker then breaks is for the running app.
/// </summary>
public class InstallerPathTextAnyPathTests
{
    // Spelled as escapes, never typed, for the reason InstallerPathTextLogPathTests
    // gives.
    private const char ZeroWidthSpace = '\u200B';
    private const char WordJoiner = '\u2060';

    private static string Treat(string? text) =>
        InstallerPathText.AllowFolderBreaksInAnyPath(text);

    [Fact]
    public void A_share_keeps_its_leading_pair_together_and_breaks_before_the_share_name()
    {
        Assert.Equal(
            @"Waiting for \" + WordJoiner + @"\fileserver.corp.example.com\" + ZeroWidthSpace
                + "Software$ to respond...",
            Treat(@"Waiting for \\fileserver.corp.example.com\Software$ to respond..."));
    }

    [Fact]
    public void A_share_at_the_start_of_the_line_is_treated_the_same()
    {
        // Where a language names the share first, as Japanese, Korean and Turkish do.
        Assert.Equal(
            @"\" + WordJoiner + @"\nas\" + ZeroWidthSpace + "apps, waiting...",
            Treat(@"\\nas\apps, waiting..."));
    }

    [Fact]
    public void A_share_needs_no_space_in_front_of_it()
    {
        Assert.Equal(
            @"(\" + WordJoiner + @"\nas\" + ZeroWidthSpace + "apps)",
            Treat(@"(\\nas\apps)"));
    }

    [Fact]
    public void Every_folder_of_a_longer_path_takes_a_break_opportunity()
    {
        Assert.Equal(
            @"Waiting for Volume{0f1e}\" + ZeroWidthSpace + @"Packages\" + ZeroWidthSpace
                + "setup.msi to respond...",
            Treat(@"Waiting for Volume{0f1e}\Packages\setup.msi to respond..."));
    }

    [Fact]
    public void A_backslash_opening_a_path_takes_no_break_after_it()
    {
        Assert.Equal(
            @"Waiting for \Packages\" + ZeroWidthSpace + "setup.msi to respond...",
            Treat(@"Waiting for \Packages\setup.msi to respond..."));
    }

    [Fact]
    public void A_destination_on_a_drive_breaks_after_each_folder()
    {
        Assert.Equal(
            @"D:\" + ZeroWidthSpace + @"Backup\" + ZeroWidthSpace + "InstallerClean",
            Treat(@"D:\Backup\InstallerClean"));
    }

    [Fact]
    public void A_destination_on_a_share_keeps_its_leading_pair_together()
    {
        Assert.Equal(
            @"\" + WordJoiner + @"\server\" + ZeroWidthSpace + @"share\" + ZeroWidthSpace + "InstallerClean",
            Treat(@"\\server\share\InstallerClean"));
    }

    [Fact]
    public void The_break_is_the_character_and_not_the_text_of_its_escape()
    {
        var result = Treat(@"D:\Backup");

        Assert.Contains(ZeroWidthSpace, result);
        Assert.DoesNotContain("u200B", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(@"D:\Backup", new string(result.Where(c => c != ZeroWidthSpace).ToArray()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Waiting for drive D: to respond...")]
    [InlineData("Checking remaining files...")]
    public void A_line_naming_no_path_is_unchanged(string text)
    {
        Assert.Equal(text, Treat(text));
    }

    [Fact]
    public void A_null_is_treated_as_empty()
    {
        Assert.Equal(string.Empty, Treat(null));
    }

    [Theory]
    [InlineData(@"Waiting for \\fileserver.corp.example.com\Software$ to respond...")]
    [InlineData(@"\\nas\apps, waiting...")]
    [InlineData(@"Waiting for Volume{0f1e}\Packages\setup.msi to respond...")]
    public void A_second_pass_changes_nothing(string text)
    {
        var once = Treat(text);

        Assert.NotEqual(text, once);
        Assert.Equal(once, Treat(once));
    }

    [Fact]
    public void Converter_treats_the_line_it_is_handed()
    {
        var result = new AnyPathTextConverter().Convert(
            @"Waiting for \\nas\apps to respond...", typeof(string), null, CultureInfo.InvariantCulture);

        // Compared as a string. Handed an object, Assert.Equal falls back to
        // string.CompareTo, a cultural comparison, which skips a joiner and a
        // zero-width space as if they were not there, so the assertion would hold
        // whether the converter added them or not.
        Assert.Equal(
            @"Waiting for \" + WordJoiner + @"\nas\" + ZeroWidthSpace + "apps to respond...",
            Assert.IsType<string>(result));
    }

    [Fact]
    public void Converter_is_one_way()
    {
        Assert.Throws<NotSupportedException>(() => new AnyPathTextConverter().ConvertBack(
            "Waiting for", typeof(string), null, CultureInfo.InvariantCulture));
    }
}
