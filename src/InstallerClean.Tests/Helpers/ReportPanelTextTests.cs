using System.Globalization;
using InstallerClean.Helpers;
using InstallerClean.Resources;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The report panel's spoken line, and the space it puts between sentences, in all
/// sixteen languages.
///
/// The line joins whole sentences that carry their own punctuation: the opening sentence
/// ends in a colon and the closing line in a stop. Where a language spaces its sentences
/// each is followed by one space; where it does not, as Japanese and Chinese do not, a
/// space after their full-width marks is a stray one. The space is read off
/// Display.SentenceSeparator (DisplayHelpers.SentenceSpace), so the tests below set what
/// the separator says against what the sentences themselves end in, which are two
/// different strings in each language and can disagree.
/// </summary>
public class ReportPanelTextTests
{
    public static TheoryData<string> Cultures()
    {
        var data = new TheoryData<string>();
        foreach (var name in SupportedLanguages.CultureNames) data.Add(name);
        return data;
    }

    /// <summary>
    /// The languages the theories run over hold the two that do not space their
    /// sentences, so a run over a shorter list cannot pass without reaching them.
    /// </summary>
    [Fact]
    public void The_languages_include_the_two_that_do_not_space_their_sentences()
    {
        Assert.Contains("ja", SupportedLanguages.CultureNames);
        Assert.Contains("zh-Hans", SupportedLanguages.CultureNames);
    }

    [Theory]
    [InlineData("en-GB", " ")]
    [InlineData("fr", " ")]
    [InlineData("ko", " ")]
    [InlineData("ja", "")]
    [InlineData("zh-Hans", "")]
    public void The_space_after_a_sentence_is_what_the_separator_ends_with(string cultureName, string expected)
    {
        using var scope = new LocalisationScope(CultureInfo.GetCultureInfo(cultureName));
        Assert.Equal(expected, DisplayHelpers.SentenceSpace, StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_spoken_line_is_the_drawn_strings_in_order(string cultureName)
    {
        using var scope = new LocalisationScope(CultureInfo.GetCultureInfo(cultureName));
        var intro = Intro();
        var line = ReportPanelText.SpokenLine(intro);

        var at = 0;
        foreach (var part in new[] { intro }
                     .Concat(ReportPanelText.Lines)
                     .Append(Strings.Completion_ReportPanel_Closing)
                     .Append(Strings.Completion_ReportPanel_SeeReport))
        {
            var found = line.IndexOf(part, at, StringComparison.Ordinal);
            Assert.True(found >= 0, $"{cultureName}: \"{part}\" is missing from, or out of order in, \"{line}\"");
            at = found + part.Length;
        }

        Assert.Equal(line.Length, at);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Each_sentence_with_its_own_mark_is_followed_by_the_space_its_language_puts_there(string cultureName)
    {
        using var scope = new LocalisationScope(CultureInfo.GetCultureInfo(cultureName));
        var intro = Intro();
        var closing = Strings.Completion_ReportPanel_Closing;
        var line = ReportPanelText.SpokenLine(intro);
        var space = DisplayHelpers.SentenceSpace;
        var faults = new List<string>();

        // The separator says whether the language spaces its sentences; the sentences
        // say it again in the mark they end with. A full-width mark carries its own
        // spacing and takes no space after it, and any other mark takes one.
        foreach (var (name, sentence) in new[] { ("the opening sentence", intro), ("the closing line", closing) })
        {
            var expected = IsFullWidth(sentence[^1]) ? "" : " ";
            if (space != expected)
                faults.Add($"{name} ends in '{sentence[^1]}' (U+{(int)sentence[^1]:X4}), which wants "
                    + $"\"{expected}\" after it, and the separator gives \"{space}\"");
        }

        var afterIntro = line[intro.Length..];
        if (!afterIntro.StartsWith(space + ReportPanelText.Lines[0], StringComparison.Ordinal))
            faults.Add($"the opening sentence is not followed by \"{space}\" and the first line");
        if (!line.EndsWith(closing + space + Strings.Completion_ReportPanel_SeeReport, StringComparison.Ordinal))
            faults.Add($"the closing line is not followed by \"{space}\" and the link's name");

        foreach (var mark in "。：？！，、")
        {
            if (line.Contains(mark + " ", StringComparison.Ordinal))
                faults.Add($"a space follows '{mark}'");
        }

        Assert.True(faults.Count == 0, $"{cultureName}: {string.Join("; ", faults)} in \"{line}\"");
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void No_stop_is_doubled(string cultureName)
    {
        using var scope = new LocalisationScope(CultureInfo.GetCultureInfo(cultureName));
        var stop = Strings.Display_SentenceSeparator.TrimEnd();
        var faults = new List<string>();

        // Each list line is ended by the separator, so a line carrying its own stop
        // would be read with two.
        foreach (var listLine in ReportPanelText.Lines)
        {
            if (listLine.EndsWith(stop, StringComparison.Ordinal))
                faults.Add($"\"{listLine}\" ends in its own '{stop}'");
        }

        var line = ReportPanelText.SpokenLine(Intro());
        if (line.Contains(stop + stop, StringComparison.Ordinal)
            || line.Contains(stop + DisplayHelpers.SentenceSpace + stop, StringComparison.Ordinal))
            faults.Add($"the line carries '{stop}' twice running");

        Assert.True(faults.Count == 0, $"{cultureName}: {string.Join("; ", faults)}");
    }

    /// <summary>
    /// The opening sentence as the window draws it and passes in: the sentence with the
    /// brackets round its link phrase taken out.
    /// </summary>
    private static string Intro()
    {
        var raw = Strings.Completion_ReportPanel_Intro;
        return CompositionParsing.SplitAtBracketedPhrase(raw) is { } split
            ? split.Prefix + split.LinkText + split.Suffix
            : raw;
    }

    /// <summary>
    /// The CJK symbols and punctuation block and the halfwidth and fullwidth forms block,
    /// which hold the full-width stop, comma, colon and question mark.
    /// </summary>
    private static bool IsFullWidth(char c) =>
        c is >= '\u3000' and <= '\u303F' or >= '\uFF00' and <= '\uFFEF';

    private sealed class LocalisationScope : IDisposable
    {
        public LocalisationScope(CultureInfo culture) => Localisation.Set(culture, culture);

        public void Dispose() => Localisation.Reset();
    }
}
