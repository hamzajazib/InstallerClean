using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using InstallerClean.Helpers;

namespace InstallerClean.Tests.Themes;

// A focus ring is drawn 2 outside the element it surrounds, and inside a
// ScrollViewer it is clipped to the viewport. These lay out a scrolling region
// shorter than its text, with a link on its first line and a link on its last,
// and move each link into view the way focus arriving on it does. The ring round
// the link has to land inside the viewport, at rest and after the region has
// scrolled to it in either direction.
public class FocusRingRoomTests
{
    // What every *FocusVisual style in Components.xaml draws outside its element.
    private const double RingOutset = 2;

    private const double Tolerance = 0.01;

    private static readonly Lazy<bool> Tracking = new(() =>
    {
        FocusRing.Track();
        return true;
    });

    [Fact]
    public void The_ring_round_a_link_at_the_top_of_the_region_is_inside_it_at_rest()
    {
        InRegion((presenter, first, _) =>
            AssertRingInside(presenter, first));
    }

    [Fact]
    public void The_ring_round_a_link_at_the_bottom_is_inside_the_region_once_it_scrolls_there()
    {
        InRegion((presenter, _, last) =>
        {
            last.BringIntoView();
            Settle(presenter);
            AssertRingInside(presenter, last);
        });
    }

    [Fact]
    public void The_ring_round_a_link_at_the_top_is_inside_the_region_once_it_scrolls_back()
    {
        InRegion((presenter, first, last) =>
        {
            last.BringIntoView();
            Settle(presenter);
            first.BringIntoView();
            Settle(presenter);
            AssertRingInside(presenter, first);
        });
    }

    /// <summary>
    /// A region 120 high holding a link at the start of its first line, 400 of
    /// blank space and a link at the start of its last line, laid out with its
    /// content carrying a margin of <see cref="FocusRing.Room"/> as the app's
    /// regions do.
    /// </summary>
    private static void InRegion(Action<ScrollContentPresenter, Hyperlink, Hyperlink> body)
    {
        LooseXaml.OnStaThread(() =>
        {
            _ = Tracking.Value;

            var first = new Hyperlink(new Run("the first link"));
            var last = new Hyperlink(new Run("the last link"));
            var content = new StackPanel { Margin = new Thickness(FocusRing.Room) };
            content.Children.Add(new TextBlock(first) { TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new Border { Height = 400 });
            content.Children.Add(new TextBlock(last) { TextWrapping = TextWrapping.Wrap });

            var viewer = new ScrollViewer
            {
                Width = 300,
                Height = 120,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content,
            };
            FocusRing.SetKeepsInView(viewer, true);

            viewer.Measure(new Size(300, 120));
            viewer.Arrange(new Rect(0, 0, 300, 120));
            Settle(viewer);

            var presenter = Assert.IsType<ScrollContentPresenter>(VisualTreeHelper.GetParent(content));
            body(presenter, first, last);
        });
    }

    // The ScrollViewer scrolls on the layout pass after a request.
    private static void Settle(UIElement element)
    {
        for (var pass = 0; pass < 4; pass++)
            element.UpdateLayout();
    }

    private static void AssertRingInside(ScrollContentPresenter presenter, Hyperlink link)
    {
        var host = (TextBlock)link.Parent;
        var viewport = new Rect(presenter.RenderSize);
        var lines = ((IContentHost)host).GetRectangles(link);
        Assert.NotEmpty(lines);

        foreach (var line in lines)
        {
            var ring = host.TransformToAncestor(presenter).TransformBounds(line);
            ring.Inflate(RingOutset, RingOutset);
            Assert.True(
                ring.Left >= viewport.Left - Tolerance && ring.Top >= viewport.Top - Tolerance
                && ring.Right <= viewport.Right + Tolerance && ring.Bottom <= viewport.Bottom + Tolerance,
                $"The ring round \"{new TextRange(link.ContentStart, link.ContentEnd).Text}\" spans {ring} "
                + $"in a viewport of {viewport}.");
        }
    }
}
