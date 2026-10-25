using System.Text.RegularExpressions;
using InstallerClean.ViewModels;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The view models a test class made. Disposing it stops the destination save each one may
/// still be waiting to make, which otherwise lands about 400 ms later in whichever test is
/// running, in the crash log every test shares. Flushing is safe on a view model a test has
/// already disposed or flushed, and throws nothing a working settings substitute would cause.
/// </summary>
internal sealed class MadeViewModels : IDisposable
{
    private readonly List<MainViewModel> _made = new();

    public MainViewModel Add(MainViewModel vm)
    {
        _made.Add(vm);
        return vm;
    }

    public void Dispose()
    {
        foreach (var vm in _made)
            vm.Cleanup.FlushMoveDestination();
    }
}

/// <summary>
/// The crash log as a view-model test compares it, kept out of AssemblyInfo.cs so that file
/// names no view-model type.
/// </summary>
internal static class ViewModelCrashLog
{
    /// <summary>Where each entry starts: a line opening "---- " (CrashLog.TryWrite).</summary>
    private static readonly Regex EntryStart = new("(?=^---- )", RegexOptions.Multiline);

    /// <summary>
    /// <see cref="TestCrashLog.Text"/> without the entries carrying
    /// <see cref="CleanupViewModel.MoveDestinationSaveRefused"/>. A view model left behind
    /// with a settings service that refuses saves writes one after a delay, into the one
    /// crash log every test shares, so a test comparing the log before and after its own
    /// work reads this and sees only entries its work could write. The text ahead of the
    /// first entry, the header a new log opens with, is kept only where an entry is kept.
    /// </summary>
    public static string WithoutDestinationSaves()
    {
        var parts = EntryStart.Split(TestCrashLog.Text());
        var kept = parts.Skip(1)
            .Where(e => !e.Contains(CleanupViewModel.MoveDestinationSaveRefused, StringComparison.Ordinal))
            .ToList();
        return kept.Count == 0 && parts.Length > 1 ? string.Empty : parts[0] + string.Concat(kept);
    }

    /// <summary>
    /// What <see cref="WithoutDestinationSaves"/> holds now beyond <paramref name="before"/>,
    /// read earlier from it: the text added to the end where the log only grew, or the whole
    /// log where a write moved a full log aside in between.
    /// </summary>
    public static string AppendedSince(string before)
    {
        var after = WithoutDestinationSaves();
        return after.StartsWith(before, StringComparison.Ordinal) ? after[before.Length..] : after;
    }
}
