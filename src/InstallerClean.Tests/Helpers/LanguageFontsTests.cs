using System.Windows.Media;
using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

public class LanguageFontsTests
{
    private static readonly string[] Montserrat = { "ru", "uk", "vi" };

    private static readonly Dictionary<string, string> EastAsian = new()
    {
        ["zh-Hans"] = "Microsoft YaHei UI, Segoe UI",
        ["ja"] = "Yu Gothic UI, Segoe UI",
        ["ko"] = "Malgun Gothic, Segoe UI",
    };

    // The Montserrat family is built against the application's pack URI. A test
    // process has loaded nothing by pack URI, so the pack scheme is registered
    // here, by PackUriHelper's first use.
    static LanguageFontsTests()
        => _ = System.IO.Packaging.PackUriHelper.Create(new Uri("application://"));

    // A composite stands in for the theme family, so its line spacing and
    // baseline are known without opening a font file.
    private static FontFamily Theme(double lineSpacing, double baseline)
    {
        var theme = new FontFamily { LineSpacing = lineSpacing, Baseline = baseline };
        theme.FamilyMaps.Add(new FontFamilyMap { Target = "Segoe UI" });
        return theme;
    }

    [Theory]
    [InlineData(1.5, 1.1)]
    [InlineData(1.4, 1.0)]
    public void East_Asian_families_take_the_theme_familys_line_spacing_and_baseline(double lineSpacing, double baseline)
    {
        LanguageFonts.Initialise(Theme(lineSpacing, baseline));

        foreach (var culture in EastAsian.Keys)
        {
            var family = LanguageFonts.For(culture);
            Assert.Equal(lineSpacing, family.LineSpacing);
            Assert.Equal(baseline, family.Baseline);
        }
    }

    [Fact]
    public void East_Asian_families_send_every_character_to_the_languages_Windows_font()
    {
        LanguageFonts.Initialise(Theme(1.5, 1.1));

        foreach (var (culture, targets) in EastAsian)
        {
            var map = Assert.Single(LanguageFonts.For(culture).FamilyMaps);
            Assert.Equal(targets, map.Target);
            Assert.Equal("0000-10ffff", map.Unicode);
            Assert.Null(map.Language);
        }
    }

    [Fact]
    public void Russian_Ukrainian_and_Vietnamese_are_drawn_in_the_bundled_Montserrat()
    {
        LanguageFonts.Initialise(Theme(1.5, 1.1));

        foreach (var culture in Montserrat)
        {
            var family = LanguageFonts.For(culture);
            Assert.Equal("./Fonts/#Montserrat, Segoe UI", family.Source);
            Assert.Equal(new Uri("pack://application:,,,/"), family.BaseUri);
        }
    }

    [Fact]
    public void Every_other_shipped_language_is_drawn_in_the_theme_family()
    {
        var theme = Theme(1.5, 1.1);
        LanguageFonts.Initialise(theme);

        var others = SupportedLanguages.CultureNames
            .Where(c => !Montserrat.Contains(c) && !EastAsian.ContainsKey(c))
            .ToList();

        Assert.Equal(SupportedLanguages.CultureNames.Count - Montserrat.Length - EastAsian.Count, others.Count);
        foreach (var culture in others)
            Assert.Same(theme, LanguageFonts.For(culture));
    }
}
