using InstallerClean.Models;
using InstallerClean.Services;
using InstallerClean.Tests.Helpers;

namespace InstallerClean.Tests.Services.Integration;

public class InstallerQueryServiceTests
{
    [Fact]
    public async Task GetRegisteredPackagesAsync_cancellation_before_start_throws()
    {
        var svc = new InstallerQueryService();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => svc.GetRegisteredPackagesAsync(cancellationToken: cts.Token));
    }

    [Fact]
    public async Task GetRegisteredPackagesAsync_cancellation_token_none_does_not_throw_cancellation()
    {
        var svc = new InstallerQueryService();

        // Default token does not interfere with the call: any thrown
        // exception in this branch is an API failure (UAE without
        // elevation), never OperationCanceledException.
        var ex = await Record.ExceptionAsync(
            () => svc.GetRegisteredPackagesAsync(cancellationToken: CancellationToken.None));

        if (ex is not null)
            Assert.IsNotType<OperationCanceledException>(ex);
    }

    [Fact]
    public async Task GetRegisteredPackagesAsync_without_elevation_throws_unauthorized()
    {
        // Non-elevated processes get AccessDenied from MsiEnumProductsEx
        // with the all-users SID. This is the expected behaviour.
        var svc = new InstallerQueryService();

        var ex = await Record.ExceptionAsync(() => svc.GetRegisteredPackagesAsync());

        // If running elevated (e.g. in CI with admin), the call succeeds.
        // If not elevated, it throws a UnauthorizedAccessException-derived
        // type (LocalisedAccessException carrying a resx-sourced message).
        if (ex is not null)
            Assert.IsAssignableFrom<UnauthorizedAccessException>(ex);
    }

    [Fact]
    public async Task GetRegisteredPackagesAsync_null_progress_does_not_throw()
    {
        var svc = new InstallerQueryService();

        // Passing null progress should not cause a NullReferenceException.
        // It may throw UnauthorizedAccessException from the API, which is fine.
        var ex = await Record.ExceptionAsync(
            () => svc.GetRegisteredPackagesAsync(progress: null));

        if (ex is not null)
        {
            Assert.IsNotType<NullReferenceException>(ex);
        }
    }

    // The tests below exercise the API on a real-elevated host. No count here:
    // one has to be true of however many there are, and a number in a comment
    // over a list is a sentence that goes stale the next time the list moves.
    // Each is marked Skip so a non-elevated CI run reports them as
    // visibly skipped rather than passing without asserting; remove
    // the Skip parameter and run elevated on Windows to exercise.
    private const string ElevatedSkipReason =
        "Manual: requires elevated Windows host. Remove [Fact(Skip)] to run.";

    [Fact(Skip = ElevatedSkipReason)]
    public async Task GetRegisteredPackagesAsync_returns_readonly_list_when_elevated()
    {
        var svc = new InstallerQueryService();
        var packages = (await svc.GetRegisteredPackagesAsync()).Packages;

        Assert.IsAssignableFrom<IReadOnlyList<RegisteredPackage>>(packages);
        Assert.NotNull(packages);
    }

    [Fact(Skip = ElevatedSkipReason)]
    public async Task GetRegisteredPackagesAsync_all_paths_non_empty_when_elevated()
    {
        var svc = new InstallerQueryService();
        var packages = (await svc.GetRegisteredPackagesAsync()).Packages;

        Assert.All(packages, p =>
            Assert.False(string.IsNullOrWhiteSpace(p.LocalPackagePath)));
    }

    [Fact(Skip = ElevatedSkipReason)]
    public async Task GetRegisteredPackagesAsync_paths_unique_case_insensitive_when_elevated()
    {
        var svc = new InstallerQueryService();
        var packages = (await svc.GetRegisteredPackagesAsync()).Packages;

        var uniquePaths = new HashSet<string>(
            packages.Select(p => p.LocalPackagePath),
            StringComparer.OrdinalIgnoreCase);

        Assert.Equal(packages.Count, uniquePaths.Count);
    }

    [Fact(Skip = ElevatedSkipReason)]
    public async Task GetRegisteredPackagesAsync_removable_only_when_superseded_when_elevated()
    {
        var svc = new InstallerQueryService();
        var packages = (await svc.GetRegisteredPackagesAsync()).Packages;

        foreach (var pkg in packages.Where(p => p.IsRemovable))
        {
            Assert.True(pkg.PatchState is 2 or 4,
                $"IsRemovable=true but PatchState={pkg.PatchState}");
        }
    }
}
