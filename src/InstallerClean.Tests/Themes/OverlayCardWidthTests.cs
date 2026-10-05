using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace InstallerClean.Tests.Themes;

// The main window's two overlay cards, the one shown while a scan runs and the one
// shown while files move or are deleted, sit over the whole window, and the window's
// width comes from the work area as well as the text size. These lay each card out
// with WPF itself at a range of window widths and text sizes and check that it stays
// inside its window. The scanning card holds a line that is replaced at each step of
// a scan, so it is also checked to be the same width whatever its line says.
//
// Each card's own width, margin, padding, border and alignment are read off
// MainWindow.xaml, and an attribute this file does not model fails the test. The
// card's children are replaced by one line of wrapping text. Its ancestors up to the
// window are required to carry nothing that narrows the room the window gives it, so
// the window's width is the room it is laid out in.
public class OverlayCardWidthTests
{
    private static readonly XNamespace Xaml = ThemeXaml.Xaml;
    private static readonly XNamespace Presentation = ThemeXaml.Presentation;

    // The text block each card's Border is found by.
    private const string ScanningCardLine = "ScanProgressText";
    private const string OperatingCardLine = "OperationHeadingText";

    private const string ShortLine = "Scanning";
    private const string LongLine =
        "Fragt Windows Installer nach den Programmen und Updates, die auf diesem PC installiert sind";

    [Theory]
    [InlineData(ScanningCardLine, 1.0)]
    [InlineData(ScanningCardLine, 1.5)]
    [InlineData(ScanningCardLine, 2.25)]
    [InlineData(OperatingCardLine, 1.0)]
    [InlineData(OperatingCardLine, 1.5)]
    [InlineData(OperatingCardLine, 2.25)]
    public void The_card_stays_inside_its_window(string lineName, double scale)
    {
        var card = Card(lineName);
        var tokens = ThemeXaml.Load("ThemeXaml.Tokens.xaml");

        OnStaThread(() =>
        {
            foreach (var windowWidth in WindowWidths())
            {
                foreach (var line in new[] { ShortLine, LongLine })
                {
                    var (left, width) = LayOut(card, tokens, scale, windowWidth, line);
                    Assert.True(left >= 0 && left + width <= windowWidth,
                        $"At {scale:P0} text in a window {windowWidth} wide, with the line \"{line}\", "
                        + $"the card spans {left} to {left + width}.");
                }
            }
        });
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.25)]
    public void The_scanning_card_is_one_width_whatever_its_line_says(double scale)
    {
        var card = Card(ScanningCardLine);
        var tokens = ThemeXaml.Load("ThemeXaml.Tokens.xaml");

        OnStaThread(() =>
        {
            foreach (var windowWidth in WindowWidths())
            {
                var (_, shortWidth) = LayOut(card, tokens, scale, windowWidth, ShortLine);
                var (_, longWidth) = LayOut(card, tokens, scale, windowWidth, LongLine);
                Assert.True(shortWidth == longWidth,
                    $"At {scale:P0} text in a window {windowWidth} wide the card is {shortWidth} wide "
                    + $"with a short line and {longWidth} with a long one.");
            }
        });
    }

    private static IEnumerable<double> WindowWidths()
    {
        for (var width = 400.0; width <= 1900; width += 25)
            yield return width;
    }

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
    /// Lays the card out in a window of the given width and returns where its left
    /// edge lands and how wide it is drawn.
    /// </summary>
    private static (double Left, double Width) LayOut(
        XElement card, XDocument tokens, double scale, double windowWidth, string line)
    {
        var border = new Border
        {
            Child = new TextBlock
            {
                Text = line,
                FontSize = 16 * scale,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
            },
        };

        foreach (var attribute in card.Attributes())
        {
            var value = attribute.Value;
            switch (attribute.Name.LocalName)
            {
                case "Width": border.Width = Length(value, scale); break;
                case "MinWidth": border.MinWidth = Length(value, scale); break;
                case "MaxWidth": border.MaxWidth = Length(value, scale); break;
                case "Margin": border.Margin = Thickness(value, tokens); break;
                case "Padding": border.Padding = Thickness(value, tokens); break;
                case "BorderThickness": border.BorderThickness = Thickness(value, tokens); break;
                case "HorizontalAlignment":
                    border.HorizontalAlignment = Enum.Parse<HorizontalAlignment>(value);
                    break;
                case "Background" or "BorderBrush" or "CornerRadius" or "VerticalAlignment":
                    break;
                default:
                    throw new InvalidOperationException(
                        $"The card sets {attribute.Name.LocalName}, which this test does not model.");
            }
        }

        var window = new Grid { Width = windowWidth };
        window.Children.Add(border);
        window.Measure(new Size(windowWidth, double.PositiveInfinity));
        window.Arrange(new Rect(0, 0, windowWidth, window.DesiredSize.Height));

        return (border.TranslatePoint(new Point(0, 0), window).X, border.ActualWidth);
    }

    /// <summary>A plain length, or one written as <c>{a11y:TextScaled N}</c>.</summary>
    private static double Length(string value, double scale)
    {
        const string scaled = "{a11y:TextScaled ";
        if (value.StartsWith(scaled, StringComparison.Ordinal) && value.EndsWith('}'))
            return double.Parse(value[scaled.Length..^1], CultureInfo.InvariantCulture) * scale;
        return double.Parse(value, CultureInfo.InvariantCulture);
    }

    /// <summary>A Thickness literal, or a <c>{StaticResource}</c> naming one in Tokens.xaml.</summary>
    private static Thickness Thickness(string value, XDocument tokens)
    {
        const string resource = "{StaticResource ";
        if (value.StartsWith(resource, StringComparison.Ordinal) && value.EndsWith('}'))
            value = ThemeXaml.ResourceValue(tokens, value[resource.Length..^1].Trim());
        return (Thickness)new ThicknessConverter().ConvertFromInvariantString(value)!;
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
