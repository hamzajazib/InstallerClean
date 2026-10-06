using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using InstallerClean.Helpers;

namespace InstallerClean.Tests.Themes;

// A scrolling region takes the focus only while it can scroll. One that has the focus
// and stops overflowing stops taking it, and WPF moves the focus up to the window,
// where no ring is drawn and Tab starts again from the first stop.
// ScrollRegionFocus.PassesFocusOn sends it to the stop Tab would go to next instead.
//
// The behaviour tests show a real window and give it the keyboard focus, which needs
// the machine running them to let a test window take the focus. Each one checks that
// first and fails saying so, so a machine that will not give the focus is told apart
// from a region that drops it.
public class ScrollRegionFocusTests
{
    [Fact]
    public void Every_scrolling_region_passes_the_focus_on()
    {
        var components = ThemeXaml.Load("ThemeXaml.Components.xaml");
        var style = components.Root!.Elements(ThemeXaml.Presentation + "Style")
            .Single(s => (string?)s.Attribute(ThemeXaml.Xaml + "Key") == "BodyScrollRegion");

        Assert.Contains(style.Elements(ThemeXaml.Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "a11y:ScrollRegionFocus.PassesFocusOn"
            && (string?)setter.Attribute("Value") == "True");
    }

    [Theory]
    [InlineData("ScanWaitingRegion", "Scan.CanStopWaiting")]
    [InlineData("OperationWaitingRegion", "Cleanup.CanStopWaiting")]
    public void A_waiting_region_is_there_only_while_a_wait_is(string region, string waiting)
    {
        var window = ThemeXaml.Load("ThemeXaml.MainWindow.xaml");
        var viewer = window.Descendants(ThemeXaml.Presentation + "ScrollViewer")
            .Single(e => (string?)e.Attribute(ThemeXaml.Xaml + "Name") == region);

        Assert.Equal($"{{Binding {waiting}, Converter={{StaticResource BoolToVis}}}}",
            (string?)viewer.Attribute("Visibility"));

        // The region is the one switch: nothing inside it is shown or hidden on its own,
        // so the button and its line can never show without the region round them.
        Assert.DoesNotContain(viewer.Descendants(), e => e.Attribute("Visibility") is not null);
    }

    [Fact]
    public void A_region_that_stops_taking_the_focus_hands_it_to_the_first_stop_inside()
    {
        InHost(passesFocusOn: true, stopInside: true, (inside, after) =>
            Assert.Same(inside, Keyboard.FocusedElement));
    }

    [Fact]
    public void A_region_with_no_stop_inside_hands_the_focus_to_the_next_stop_after_it()
    {
        InHost(passesFocusOn: true, stopInside: false, (inside, after) =>
            Assert.Same(after, Keyboard.FocusedElement));
    }

    // The control: the same region without the property loses the focus to neither
    // button, which is what the two tests above would see if the property did nothing.
    [Fact]
    public void Without_the_property_the_focus_goes_to_neither_stop()
    {
        InHost(passesFocusOn: false, stopInside: true, (inside, after) =>
        {
            Assert.NotSame(inside, Keyboard.FocusedElement);
            Assert.NotSame(after, Keyboard.FocusedElement);
        });
    }

    /// <summary>
    /// A scrolling region, with a button inside it when <paramref name="stopInside"/> is
    /// set, and a button after it, shown in a window of their own. The region is given the
    /// keyboard focus and then stops taking it, as the BodyScrollRegion style's trigger
    /// makes it do when its content stops overflowing, and <paramref name="assert"/> runs
    /// once everything that change posted has run.
    ///
    /// The window is a bare HwndSource rather than a WPF Window: what the focus needs is a
    /// presentation source with a window handle, and a Window built in code is what
    /// scripts/check-xaml-font.mjs refuses, the app's own windows being XAML roots that
    /// carry its font.
    /// </summary>
    private static void InHost(bool passesFocusOn, bool stopInside, Action<Button?, Button> assert)
    {
        LooseXaml.OnStaThread(() =>
        {
            var inside = stopInside ? new Button { Content = "inside" } : null;
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = "text" });
            if (inside is not null) content.Children.Add(inside);

            var viewer = new ScrollViewer { Content = content, Height = 100 };
            ScrollRegionFocus.SetPassesFocusOn(viewer, passesFocusOn);

            var after = new Button { Content = "after" };
            var panel = new StackPanel();
            panel.Children.Add(viewer);
            panel.Children.Add(after);

            HwndSource? host = null;
            try
            {
                host = new HwndSource(new HwndSourceParameters(nameof(ScrollRegionFocusTests), 300, 300))
                {
                    RootVisual = panel,
                };
                Settle();

                Keyboard.Focus(viewer);
                Assert.True(viewer.IsKeyboardFocused,
                    "This machine did not give the test window the keyboard focus, so nothing "
                    + "about the region could be checked.");

                viewer.Focusable = false;
                Settle();

                assert(inside, after);
            }
            finally
            {
                host?.Dispose();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
    }

    /// <summary>
    /// Runs everything queued above context-idle priority, which includes the move posted
    /// at Normal and WPF's own focus check posted at Input. Bounded, so nothing queued can
    /// hold the run.
    /// </summary>
    private static void Settle() =>
        Dispatcher.CurrentDispatcher.Invoke(
            () => { }, DispatcherPriority.ContextIdle, CancellationToken.None, TimeSpan.FromSeconds(5));
}
