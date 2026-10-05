using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;

namespace InstallerClean.Tests.Themes;

/// <summary>
/// XAML from the app's own files, made ready for a XamlReader and laid out with WPF
/// itself, for the tests in this folder that measure where an element is drawn.
/// </summary>
internal static class LooseXaml
{
    /// <summary>
    /// <paramref name="xaml"/> with the app's own namespaces named with the assembly
    /// that holds them, which the compiled XAML does not need and a XamlReader does,
    /// and every <c>{a11y:TextScaled N}</c> replaced by N multiplied by
    /// <paramref name="scale"/>.
    /// </summary>
    internal static string Prepared(string xaml, double scale)
    {
        foreach (var name in new[] { "Resources", "Helpers", "Controls" })
            xaml = xaml.Replace($"\"clr-namespace:InstallerClean.{name}\"",
                $"\"clr-namespace:InstallerClean.{name};assembly=InstallerClean\"", StringComparison.Ordinal);

        return Regex.Replace(xaml, @"\{a11y:TextScaled ([0-9.]+)\}", match =>
            (double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * scale)
                .ToString("R", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Runs <paramref name="body"/> on a thread of its own in a single-threaded
    /// apartment, which WPF elements need, and rethrows anything it threw.
    /// </summary>
    internal static void OnStaThread(Action body)
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
