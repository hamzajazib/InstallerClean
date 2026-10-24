using InstallerClean.Services;
using Microsoft.Win32;

namespace InstallerClean.Tests.Services.Integration;

/// <summary>
/// <see cref="FirstRunMark"/> against the real registry, on a scratch key under
/// HKCU that each test creates under its own name and deletes afterwards, so it
/// needs no administrator rights and leaves nothing behind.
/// </summary>
public sealed class FirstRunMarkTests : IDisposable
{
    // One level under Software, so the key Dispose deletes is the only one a test creates.
    private readonly string _keyPath =
        @"Software\InstallerClean-test-FirstRunMark-" + Guid.NewGuid().ToString("N");

    private FirstRunMark Mark => new(RegistryHive.CurrentUser, _keyPath);

    public void Dispose() =>
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);

    [Fact]
    public void A_key_that_is_not_there_reads_as_not_set()
    {
        Assert.Equal(FirstRunMarkState.NotSet, Mark.Read());
    }

    [Fact]
    public void A_key_without_the_value_reads_as_not_set()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(_keyPath))
            key.SetValue("SomethingElse", 1, RegistryValueKind.DWord);

        Assert.Equal(FirstRunMarkState.NotSet, Mark.Read());
    }

    [Fact]
    public void Setting_it_creates_the_key_and_writes_a_dword_of_one()
    {
        Mark.Set();

        using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
        Assert.NotNull(key);
        Assert.Equal(RegistryValueKind.DWord, key.GetValueKind(FirstRunMark.ValueName));
        Assert.Equal(1, key.GetValue(FirstRunMark.ValueName));
        Assert.Equal(FirstRunMarkState.Set, Mark.Read());
    }

    [Fact]
    public void Setting_it_again_leaves_it_set()
    {
        Mark.Set();
        Mark.Set();

        Assert.Equal(FirstRunMarkState.Set, Mark.Read());
    }

    [Theory]
    [InlineData(0, RegistryValueKind.DWord)]
    [InlineData("0", RegistryValueKind.String)]
    [InlineData("", RegistryValueKind.String)]
    public void Any_value_under_the_name_reads_as_set(object value, RegistryValueKind kind)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(_keyPath))
            key.SetValue(FirstRunMark.ValueName, value, kind);

        Assert.Equal(FirstRunMarkState.Set, Mark.Read());
    }

    // Each claim test names a fresh Local\ lock, never FirstRunMark.ClaimLockName, so a copy of
    // InstallerClean running on the machine neither fails a test nor is held up by one.
    private readonly string _claimLock = $@"Local\InstallerClean_FirstRunMarkClaimTest_{Guid.NewGuid():N}";

    private FirstRunMark ClaimingMark(TimeSpan? wait = null) =>
        new(RegistryHive.CurrentUser, _keyPath, _claimLock, wait ?? TimeSpan.FromSeconds(10));

    [Fact]
    public void The_first_claim_sets_the_mark_and_the_next_is_refused()
    {
        Assert.True(ClaimingMark().TryClaim());
        Assert.Equal(FirstRunMarkState.Set, Mark.Read());

        Assert.False(ClaimingMark().TryClaim());
    }

    [Fact]
    public void A_claim_on_a_mark_that_is_already_set_is_refused()
    {
        Mark.Set();

        Assert.False(ClaimingMark().TryClaim());
    }

    [Fact]
    public void A_mark_this_instance_set_counts_as_clear_for_one_claim_only()
    {
        // A command-line Delete or Move that acts sets the mark as its batch returns,
        // and claims the report of that same run afterwards.
        var mark = ClaimingMark();
        mark.Set();

        Assert.True(mark.TryClaim());
        Assert.False(mark.TryClaim());
        Assert.False(ClaimingMark().TryClaim());
    }

    [Fact]
    public void Writing_a_mark_that_is_already_there_does_not_make_it_this_instances_own()
    {
        // A scan claims the report while a Delete is still deleting; the Delete then sets
        // the mark as its batch returns, and its claim finds the mark was not its own.
        Assert.True(ClaimingMark().TryClaim());
        var later = ClaimingMark();
        later.Set();

        Assert.False(later.TryClaim());
    }

    [Fact]
    public void Of_claims_made_together_on_one_instance_exactly_one_is_granted()
    {
        const int Claimants = 16;
        using var start = new Barrier(Claimants);
        var mark = ClaimingMark();
        var granted = 0;

        var threads = Enumerable.Range(0, Claimants).Select(_ => new Thread(() =>
        {
            start.SignalAndWait();
            if (mark.TryClaim()) Interlocked.Increment(ref granted);
        })).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        Assert.Equal(1, granted);
    }

    [Fact]
    public void Of_claims_made_together_against_one_mark_exactly_one_is_granted()
    {
        const int Claimants = 16;
        using var start = new Barrier(Claimants);
        var granted = 0;

        var threads = Enumerable.Range(0, Claimants).Select(_ => new Thread(() =>
        {
            var mark = ClaimingMark();
            start.SignalAndWait();
            if (mark.TryClaim()) Interlocked.Increment(ref granted);
        })).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        Assert.Equal(1, granted);
        Assert.Equal(FirstRunMarkState.Set, Mark.Read());
    }

    [Fact]
    public void A_claim_that_cannot_get_the_lock_in_time_is_refused_and_leaves_the_mark_clear()
    {
        var taken = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using var held = new Mutex(initiallyOwned: false, _claimLock);
            held.WaitOne();
            taken.Set();
            release.Wait();
            held.ReleaseMutex();
        });
        holder.Start();
        taken.Wait();

        var granted = ClaimingMark(TimeSpan.FromMilliseconds(50)).TryClaim();
        release.Set();
        holder.Join();

        Assert.False(granted);
        Assert.Equal(FirstRunMarkState.NotSet, Mark.Read());
    }

    [Fact]
    public void A_claim_leaves_the_lock_free_for_the_next()
    {
        Assert.True(ClaimingMark().TryClaim());

        var free = false;
        var thread = new Thread(() =>
        {
            using var other = new Mutex(initiallyOwned: false, _claimLock);
            free = other.WaitOne(TimeSpan.Zero);
            if (free) other.ReleaseMutex();
        });
        thread.Start();
        thread.Join();

        Assert.True(free);
    }

    [Fact]
    public void The_claim_lock_is_not_the_single_instance_lock()
    {
        Assert.Equal(@"Global\InstallerClean_FirstReportClaim", FirstRunMark.ClaimLockName);
        Assert.NotEqual(InstanceLock.Name, FirstRunMark.ClaimLockName);
    }

    [Fact]
    public void The_mark_is_FirstRunRecorded_under_the_NoFaff_key()
    {
        // The installer's note names this key and value.
        Assert.Equal(@"SOFTWARE\NoFaff\InstallerClean", FirstRunMark.KeyPath);
        Assert.Equal("FirstRunRecorded", FirstRunMark.ValueName);
    }
}
