using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;

namespace InstallerClean.Tests.Themes;

// Each Details window has a band of two columns with a gap between: the unneeded
// files' window its list and the selected file's details, the registered files'
// window a product's patches and its details. One of the two stops at a scaled
// width. These lay the band out with WPF itself, at a range of window widths and
// text sizes, and check that neither column runs past the window, that while the
// capped column is short of its cap the other is never narrower than it, and that
// wherever the window is wide enough for the cap twice over the capped column is
// exactly its cap, which is the layout the window opens in.
//
// The band's own column definitions are parsed from the window's XAML. Its children
// are replaced by empty Borders carrying only their column and their own widths:
// their contents carry event handlers a XamlReader cannot take, and the columns do
// not depend on them. The room is the window's width less the margin of the grid
// the band sits in, read from Tokens.xaml; any other ancestor carrying a width, a
// margin, padding or columns fails the test rather than being measured wrongly.
public class DetailsWindowColumnTests
{
    private static readonly XNamespace Xaml = ThemeXaml.Xaml;
    private static readonly XNamespace Presentation = ThemeXaml.Presentation;

    // Room for the rounding in WPF's arithmetic when it divides a length.
    private const double Tolerance = 0.01;

    [Theory]
    [InlineData("ThemeXaml.OrphanedFilesWindow.xaml", 1.0)]
    [InlineData("ThemeXaml.OrphanedFilesWindow.xaml", 1.5)]
    [InlineData("ThemeXaml.OrphanedFilesWindow.xaml", 2.25)]
    [InlineData("ThemeXaml.RegisteredFilesWindow.xaml", 1.0)]
    [InlineData("ThemeXaml.RegisteredFilesWindow.xaml", 1.5)]
    [InlineData("ThemeXaml.RegisteredFilesWindow.xaml", 2.25)]
    public void The_two_columns_share_a_narrow_window_and_neither_runs_past_it(string resource, double scale)
    {
        var (band, paneColumn, otherColumn, cap, sideMargins) = Band(resource);
        var xaml = LooseXaml.Prepared(band.ToString(SaveOptions.DisableFormatting), scale);
        var failures = new List<string>();

        LooseXaml.OnStaThread(() =>
        {
            var grid = (Grid)XamlReader.Parse(xaml);
            for (var windowWidth = 400.0; windowWidth <= 2400; windowWidth += 25)
            {
                var room = windowWidth - sideMargins;
                grid.Width = room;
                grid.Measure(new Size(room, double.PositiveInfinity));
                grid.Arrange(new Rect(0, 0, room, grid.DesiredSize.Height));

                var at = $"{resource} at {scale:P0} in a window {windowWidth} wide";
                foreach (var child in grid.Children.OfType<FrameworkElement>())
                {
                    var left = child.TranslatePoint(new Point(0, 0), grid).X;
                    if (left < -Tolerance || left + child.ActualWidth > room + Tolerance)
                        failures.Add($"{at}: column {Grid.GetColumn(child)} spans {left} to "
                            + $"{left + child.ActualWidth} in a room {room} wide.");
                }

                var pane = grid.ColumnDefinitions[paneColumn].ActualWidth;
                var other = grid.ColumnDefinitions[otherColumn].ActualWidth;
                var gaps = room - pane - other;
                if (pane < cap * scale - Tolerance && other < pane - Tolerance)
                    failures.Add($"{at}: the capped column is {pane}, under its cap, and the other only {other}.");
                if (room >= 2 * cap * scale + gaps && Math.Abs(pane - cap * scale) > Tolerance)
                    failures.Add($"{at}: the capped column is {pane}, not its cap of {cap * scale}.");
            }
        });

        Assert.True(failures.Count == 0, $"{failures.Count} failures, the first of them:{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures.Take(40)));
    }

    /// <summary>
    /// The band in <paramref name="resource"/>, reduced to its column definitions and
    /// one empty Border per child; which column is capped and which is the other; the
    /// cap at 100% text scale; and the side margins between the window and the band.
    /// </summary>
    private static (XElement Band, int PaneColumn, int OtherColumn, double Cap, double SideMargins) Band(
        string resource)
    {
        var window = ThemeXaml.Load(resource).Root!;
        var grids = window.Descendants(Presentation + "Grid")
            .Where(g => g.Element(Presentation + "Grid.ColumnDefinitions")?.Elements().Count() == 3
                && g.Ancestors(Presentation + "DataTemplate").FirstOrDefault() is null
                && g.Ancestors(Presentation + "ScrollViewer").FirstOrDefault() is null
                && g.Ancestors(Presentation + "Border").FirstOrDefault() is null)
            .ToList();
        var original = Assert.Single(grids);

        // The capped column is the one whose definition carries a MaxWidth, or, in a
        // band that sizes a column to a child of fixed width instead, that child's.
        var definitions = original.Element(Presentation + "Grid.ColumnDefinitions")!.Elements().ToList();
        var children = original.Elements().Where(e => !e.Name.LocalName.Contains('.')).ToList();
        var capped = definitions.FindIndex(d => d.Attribute("MaxWidth") is not null);
        var capText = capped >= 0 ? (string?)definitions[capped].Attribute("MaxWidth") : null;
        if (capped < 0 && children.SingleOrDefault(c => c.Attribute("Width") is not null) is { } fixedChild)
        {
            capped = int.Parse((string?)fixedChild.Attribute("Grid.Column") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
            capText = (string)fixedChild.Attribute("Width")!;
        }
        Assert.True(capped is 0 or 2, $"No column at either end of the band in {resource} stops at a width of its own.");
        Assert.StartsWith("{a11y:TextScaled ", capText);
        var cap = double.Parse(capText!["{a11y:TextScaled ".Length..^1], System.Globalization.CultureInfo.InvariantCulture);
        var otherColumn = capped == 0 ? 2 : 0;

        var band = new XElement(Presentation + "Grid",
            new XAttribute("xmlns", Presentation.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "x", Xaml.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a11y", "clr-namespace:InstallerClean.Helpers"),
            new XElement(original.Element(Presentation + "Grid.ColumnDefinitions")!));
        foreach (var child in children)
            band.Add(new XElement(Presentation + "Border",
                child.Attributes().Where(a => a.Name.LocalName is "Grid.Column" or "Width" or "MinWidth" or "MaxWidth")));

        var tokens = ThemeXaml.Load("ThemeXaml.Tokens.xaml");
        var sideMargins = 0.0;
        foreach (var ancestor in original.Ancestors().TakeWhile(a => a.Name != Presentation + "Window"))
        {
            foreach (var property in new[] { "Width", "MinWidth", "MaxWidth", "Padding" })
                Assert.True(ancestor.Attribute(property) is null,
                    $"The band's ancestor {ancestor.Name.LocalName} in {resource} sets {property}, which this test does not model.");
            Assert.True(ancestor.Element(Presentation + "Grid.ColumnDefinitions") is null,
                $"The band's ancestor {ancestor.Name.LocalName} in {resource} has columns, which this test does not model.");
            if (ancestor.Attribute("Margin") is { } margin)
            {
                var value = margin.Value.StartsWith("{StaticResource ", StringComparison.Ordinal)
                    ? ThemeXaml.ResourceValue(tokens, margin.Value["{StaticResource ".Length..^1].Trim())
                    : margin.Value;
                var thickness = (Thickness)new ThicknessConverter().ConvertFromInvariantString(value)!;
                sideMargins += thickness.Left + thickness.Right;
            }
        }

        return (band, capped, otherColumn, cap, sideMargins);
    }
}
