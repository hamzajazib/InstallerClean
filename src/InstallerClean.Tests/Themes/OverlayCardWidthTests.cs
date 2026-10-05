using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using InstallerClean.Helpers;

namespace InstallerClean.Tests.Themes;

// The main window's two overlay cards, the one shown while a scan runs and the one
// shown while files move or are deleted, sit over the whole window, and the window's
// width comes from the work area as well as the text size. These lay each card out
// with WPF itself, as MainWindow.xaml writes it, in every language, at a range of
// window widths and text sizes, and check that the card stays inside its window and
// everything in it stays inside the card. The scanning card holds a line that is
// replaced at each step of a scan, so it is also checked to be the same width
// whatever its line says.
//
// Each card is parsed from MainWindow.xaml under the theme's own resources, with the
// text sizes App.xaml.cs scales multiplied by the text size, every TextScaled length
// multiplied by it too, and the language's own font family loaded from the app's font
// files. Its ancestors up to the window are required to carry nothing that narrows
// the room the window gives it, so the window's width is the room it is laid out in.
// With no data context every binding falls back to its default, so every row the card
// can show is shown, which is the widest the card gets.
public class OverlayCardWidthTests
{
    private static readonly XNamespace Xaml = ThemeXaml.Xaml;
    private static readonly XNamespace Presentation = ThemeXaml.Presentation;

    // The text block each card's Border is found by.
    private const string ScanningCardLine = "ScanProgressText";
    private const string OperatingCardLine = "OperationHeadingText";

    // Room for the rounding in WPF's arithmetic when it centres one length in another.
    private const double Tolerance = 0.01;

    private const string ShortLine = "Scanning";
    private const string LongLine =
        "Fragt Windows Installer nach den Programmen und Updates, die auf diesem PC installiert sind";

    // The app's font files, copied beside this assembly by the csproj. The theme
    // names them by the application's pack URI, which needs a running Application to
    // resolve, so here they are named by their place on disk instead.
    private static readonly Uri FontBase = new(AppContext.BaseDirectory.TrimEnd('\\', '/') + "/");

    // LanguageFonts builds the Montserrat family against the application's pack URI,
    // and a test process has loaded nothing by pack URI, so the pack scheme is
    // registered here, by PackUriHelper's first use.
    static OverlayCardWidthTests()
        => _ = System.IO.Packaging.PackUriHelper.Create(new Uri("application://"));

    [Theory]
    [InlineData(ScanningCardLine, 1.0)]
    [InlineData(ScanningCardLine, 1.5)]
    [InlineData(ScanningCardLine, 2.25)]
    [InlineData(OperatingCardLine, 1.0)]
    [InlineData(OperatingCardLine, 1.5)]
    [InlineData(OperatingCardLine, 2.25)]
    public void The_card_and_everything_in_it_stay_inside_the_window_in_every_language(
        string lineName, double scale)
    {
        var failures = new List<string>();

        foreach (var language in SupportedLanguages.CultureNames)
        {
            InLanguage(language, scale, lineName, (host, card) =>
            {
                ((TextBlock)card.FindName(lineName)).Text = LongLine;
                var rows = (Panel)card.Child;
                foreach (var windowWidth in WindowWidths())
                {
                    LayOut(host, windowWidth);

                    var left = card.TranslatePoint(new Point(0, 0), host).X;
                    if (left < -Tolerance || left + card.ActualWidth > windowWidth + Tolerance)
                        failures.Add($"{language} at {scale:P0} in a window {windowWidth} wide: "
                            + $"the card spans {left} to {left + card.ActualWidth}.");

                    foreach (var element in Shown(rows))
                    {
                        var start = element.TranslatePoint(new Point(0, 0), rows).X;
                        if (start < -Tolerance || start + element.ActualWidth > rows.ActualWidth + Tolerance)
                            failures.Add($"{language} at {scale:P0} in a window {windowWidth} wide: "
                                + $"{Describe(element)} spans {start} to {start + element.ActualWidth} "
                                + $"inside a card {rows.ActualWidth} wide.");
                    }
                }
            });
        }

        Assert.True(failures.Count == 0, Report(failures));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.25)]
    public void The_scanning_card_is_one_width_whatever_its_line_says(double scale)
    {
        var failures = new List<string>();

        foreach (var language in SupportedLanguages.CultureNames)
        {
            InLanguage(language, scale, ScanningCardLine, (host, card) =>
            {
                var line = (TextBlock)card.FindName(ScanningCardLine);
                foreach (var windowWidth in WindowWidths())
                {
                    line.Text = ShortLine;
                    LayOut(host, windowWidth);
                    var shortWidth = card.ActualWidth;

                    line.Text = LongLine;
                    LayOut(host, windowWidth);
                    var longWidth = card.ActualWidth;

                    if (shortWidth != longWidth)
                        failures.Add($"{language} at {scale:P0} in a window {windowWidth} wide: the card is "
                            + $"{shortWidth} wide with a short line and {longWidth} with a long one.");
                }
            });
        }

        Assert.True(failures.Count == 0, Report(failures));
    }

    private static string Report(List<string> failures)
        => $"{failures.Count} failures, the first of them:{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures.Take(40));

    private static IEnumerable<double> WindowWidths()
    {
        for (var width = 400.0; width <= 1900; width += 25)
            yield return width;
    }

    private static void LayOut(Grid host, double windowWidth)
    {
        host.Width = windowWidth;
        host.Measure(new Size(windowWidth, double.PositiveInfinity));
        host.Arrange(new Rect(0, 0, windowWidth, host.DesiredSize.Height));
    }

    /// <summary>
    /// Every element under <paramref name="parent"/> in the logical tree, the card's
    /// own rows and what they hold, that is not collapsed or hidden.
    /// </summary>
    private static IEnumerable<FrameworkElement> Shown(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<FrameworkElement>())
        {
            if (child.Visibility != Visibility.Visible)
                continue;
            yield return child;
            foreach (var descendant in Shown(child))
                yield return descendant;
        }
    }

    private static string Describe(FrameworkElement element)
        => string.IsNullOrEmpty(element.Name) ? element.GetType().Name : $"{element.GetType().Name} {element.Name}";

    /// <summary>
    /// Parses the card under the theme in <paramref name="language"/> at
    /// <paramref name="scale"/>, in that language's font, and hands it and the grid
    /// standing for its window to <paramref name="body"/> on a thread WPF can use.
    /// </summary>
    private static void InLanguage(string language, double scale, string lineName, Action<Grid, Border> body)
    {
        var xaml = ComposedXaml(Card(lineName), scale);
        var culture = CultureInfo.GetCultureInfo(language);

        OnStaThread(() =>
        {
            Localisation.Set(culture, culture);
            try
            {
                var host = (Grid)XamlReader.Parse(xaml);
                host.Language = XmlLanguage.GetLanguage(language);
                host.Resources["Type.FontFamily"] = LanguageFamily(language);
                body(host, (Border)host.Children[0]);
            }
            finally
            {
                Localisation.Reset();
            }
        });
    }

    /// <summary>
    /// The family the app draws <paramref name="language"/> in, from LanguageFonts,
    /// with the app's own font files named by their place on disk. Refuses a family
    /// whose first face resolves anywhere but where it names, so the widths measured
    /// are never a fallback font's.
    /// </summary>
    private static FontFamily LanguageFamily(string language)
    {
        var theme = new FontFamily(FontBase, OnDisk(ThemeFamilySource()));
        LanguageFonts.Initialise(theme);
        var family = LanguageFonts.For(language);

        if (family.Source is null)
        {
            // A composite of a Windows interface font, built in code.
            var target = family.FamilyMaps.Single().Target.Split(',')[0].Trim();
            Assert.True(Fonts.SystemFontFamilies.Any(f => f.Source == target),
                $"{language} is drawn in {target}, which this machine does not have.");
            return family;
        }

        if (family.BaseUri?.Scheme == "pack")
            family = new FontFamily(FontBase, family.Source);

        var first = family.Source.Split(',')[0].Trim();
        var named = first[(first.IndexOf('#') + 1)..];
        var face = new Typeface(new FontFamily(family.BaseUri, first),
            FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
        Assert.True(face.TryGetGlyphTypeface(out var glyphs),
            $"{language}'s first family, {first}, did not resolve to a font file.");
        Assert.True(Path.GetFileName(glyphs.FontUri.LocalPath).StartsWith(named + "-", StringComparison.Ordinal),
            $"{language}'s first family, {first}, resolved to {glyphs.FontUri}.");
        return family;
    }

    private static string ThemeFamilySource()
        => ThemeXaml.ResourceValue(ThemeXaml.Load("ThemeXaml.Tokens.xaml"), "Type.FontFamily");

    private static string OnDisk(string familySource)
        => familySource.Replace("pack://application:,,,/", "./", StringComparison.Ordinal);

    /// <summary>The Border holding the named text block.</summary>
    private static XElement Card(string lineName)
    {
        var window = ThemeXaml.Load("ThemeXaml.MainWindow.xaml");
        var line = window.Descendants(Presentation + "TextBlock")
            .Single(e => (string?)e.Attribute(Xaml + "Name") == lineName);
        var card = line.Ancestors(Presentation + "Border").First();

        foreach (var ancestor in card.Ancestors().TakeWhile(a => a.Name != Presentation + "Window"))
        {
            foreach (var property in new[] { "Width", "MinWidth", "MaxWidth", "Margin", "Padding" })
                Assert.True(ancestor.Attribute(property) is null,
                    $"The card's ancestor {ancestor.Name.LocalName} sets {property}, which this test does not model.");
            Assert.True(ancestor.Element(Presentation + "Grid.ColumnDefinitions") is null,
                $"The card's ancestor {ancestor.Name.LocalName} has columns, which this test does not model.");
        }

        return card;
    }

    /// <summary>
    /// One grid standing for the window: the theme's resources, the window's own,
    /// and the card, as a single XAML document a XamlReader can parse.
    /// </summary>
    private static string ComposedXaml(XElement card, double scale)
    {
        var window = ThemeXaml.Load("ThemeXaml.MainWindow.xaml");
        var resources = new XElement(Presentation + "Grid.Resources");

        foreach (var theme in new[] { "Primitives", "Tokens", "Components" })
            resources.Add(ThemeXaml.Load($"ThemeXaml.{theme}.xaml").Root!.Elements());

        // The theme's family, with the app's font files named by their place on disk.
        var family = resources.Elements().Single(e => (string?)e.Attribute(Xaml + "Key") == "Type.FontFamily");
        family.Value = family.Value.Replace("pack://application:,,,/", FontBase.AbsoluteUri, StringComparison.Ordinal);

        // The six text sizes App.xaml.cs multiplies by the text size.
        var scaled = (string[])typeof(App)
            .GetField("TypeSizeTokenKeys", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        foreach (var key in scaled)
        {
            var size = resources.Elements().Single(e => (string?)e.Attribute(Xaml + "Key") == key);
            size.Value = (double.Parse(size.Value, CultureInfo.InvariantCulture) * scale)
                .ToString("R", CultureInfo.InvariantCulture);
        }

        // The window's converters rewrite a bound value only, and with no data
        // context no value reaches them. The app's own are internal, which a
        // XamlReader cannot build, so each stands in under its key as a framework one.
        foreach (var converter in window.Root!.Element(Presentation + "Window.Resources")!.Elements())
            resources.Add(converter.Name.Namespace == Presentation
                ? new XElement(converter)
                : new XElement(Presentation + "BooleanToVisibilityConverter",
                    new XAttribute(Xaml + "Key", (string)converter.Attribute(Xaml + "Key")!)));

        var root = new XElement(Presentation + "Grid",
            new XAttribute("xmlns", Presentation.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "x", Xaml.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "sys", "clr-namespace:System;assembly=mscorlib"),
            new XAttribute(XNamespace.Xmlns + "loc", "clr-namespace:InstallerClean.Resources"),
            new XAttribute(XNamespace.Xmlns + "a11y", "clr-namespace:InstallerClean.Helpers"),
            new XAttribute(XNamespace.Xmlns + "chrome", "clr-namespace:InstallerClean.Controls"),
            new XAttribute("TextElement.FontFamily", "{DynamicResource Type.FontFamily}"),
            resources,
            new XElement(card));

        // The app's own namespaces are named with the assembly that holds them, which
        // the compiled XAML does not need and a XamlReader does.
        var text = root.ToString(SaveOptions.DisableFormatting);
        foreach (var name in new[] { "Resources", "Helpers", "Controls" })
            text = text.Replace($"\"clr-namespace:InstallerClean.{name}\"",
                $"\"clr-namespace:InstallerClean.{name};assembly=InstallerClean\"", StringComparison.Ordinal);

        return Regex.Replace(text, @"\{a11y:TextScaled ([0-9.]+)\}", match =>
            (double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * scale)
                .ToString("R", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Runs <paramref name="body"/> on a thread of its own in a single-threaded
    /// apartment, which WPF elements need, and rethrows anything it threw.
    /// </summary>
    private static void OnStaThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
