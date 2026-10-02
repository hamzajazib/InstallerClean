using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The parsing behind the windows' inline-composition builders, extracted out of
/// the <c>Window</c> constructors so it has a seam to test against.
/// </summary>
public class CompositionParsingTests
{
    [Fact]
    public void SplitAtSubstring_splits_around_the_substring_and_trims_the_prefix()
    {
        var split = CompositionParsing.SplitAtSubstring(@"Moved 3 files to: D:\Backup", @"D:\Backup");

        Assert.NotNull(split);
        Assert.Equal("Moved 3 files to:", split!.Prefix);
        Assert.Equal(string.Empty, split.Suffix);
    }

    [Fact]
    public void SplitAtSubstring_keeps_the_text_on_both_sides()
    {
        var split = CompositionParsing.SplitAtSubstring("before HERE after", "HERE");

        Assert.NotNull(split);
        Assert.Equal("before", split!.Prefix);
        Assert.Equal(" after", split.Suffix);
    }

    [Fact]
    public void SplitAtSubstring_returns_null_when_the_substring_is_absent()
    {
        Assert.Null(CompositionParsing.SplitAtSubstring("no path in this sentence", @"D:\Backup"));
    }

    [Fact]
    public void SplitAtSubstring_returns_null_for_an_empty_substring()
    {
        // The all-clear and delete-to-bin summaries carry no destination, so the
        // whole line must render as one Run rather than splitting on "".
        Assert.Null(CompositionParsing.SplitAtSubstring("Nothing to clean up", string.Empty));
    }

    [Fact]
    public void SplitAtBracketedPhrase_splits_prefix_link_and_suffix()
    {
        var split = CompositionParsing.SplitAtBracketedPhrase(
            @"Copy them back to C:\Windows\Installer if anything ever breaks ([extremely unlikely]).");

        Assert.NotNull(split);
        Assert.Equal(@"Copy them back to C:\Windows\Installer if anything ever breaks (", split!.Prefix);
        Assert.Equal("extremely unlikely", split.LinkText);
        Assert.Equal(").", split.Suffix);
    }

    [Fact]
    public void SplitAtBracketedPhrase_handles_a_leading_bracket()
    {
        var split = CompositionParsing.SplitAtBracketedPhrase("[learn more] about this");

        Assert.NotNull(split);
        Assert.Equal(string.Empty, split!.Prefix);
        Assert.Equal("learn more", split.LinkText);
        Assert.Equal(" about this", split.Suffix);
    }

    [Fact]
    public void SplitAtBracketedPhrase_returns_null_without_a_complete_pair()
    {
        // The all-clear receipt and the permanent-delete reassurance carry no
        // link, so they must render verbatim.
        Assert.Null(CompositionParsing.SplitAtBracketedPhrase("A plain sentence with no link."));
        Assert.Null(CompositionParsing.SplitAtBracketedPhrase("An open [ bracket with no close."));
    }
}
