using System.ComponentModel;
using System.Net.Sockets;
using System.Reflection;
using ToolsBox.App.Ports;
using ToolsBox.Core.Ports;

namespace ToolsBox.App.Tests.Ports;

public sealed class PortMonitorViewModelTests
{
    [Fact]
    public async Task StartupAndInputChangesNeverScanUntilExplicitlyRequested()
    {
        var provider = new Provider();
        using var vm = new PortMonitorViewModel(provider);
        await vm.InitializeAsync();
        vm.RefreshIntervalSeconds = 1;
        vm.SearchText = "8080";
        await Task.Delay(1150);
        Assert.True(vm.IsPaused);
        Assert.Equal(0, provider.Calls);
        Assert.Empty(vm.Entries);
        Assert.Equal("尚未刷新", vm.LastRefreshText);
    }

    [Fact]
    public async Task RefreshOnceDoesNotStartAutomaticRefresh()
    {
        var provider = new Provider();
        using var vm = new PortMonitorViewModel(provider);
        await vm.InitializeAsync();
        vm.RefreshIntervalSeconds = 1;
        vm.RefreshCommand.Execute(null);
        await Until(() => provider.Calls == 1 && !vm.IsRefreshing);
        Assert.True(vm.IsPaused);
        Assert.Equal(2, vm.Entries.Count);
        await Task.Delay(1150);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task ExplicitAutomaticStartScansImmediatelyAndPauseStopsTicks()
    {
        var provider = new Provider();
        using var vm = new PortMonitorViewModel(provider);
        await vm.InitializeAsync();
        vm.RefreshIntervalSeconds = 1;
        vm.TogglePauseCommand.Execute(null);
        await Until(() => provider.Calls >= 1);
        Assert.False(vm.IsPaused);
        await Until(() => provider.Calls >= 2);
        vm.TogglePauseCommand.Execute(null);
        int pausedCalls = provider.Calls;
        await Task.Delay(1150);
        Assert.Equal(pausedCalls, provider.Calls);
        Assert.True(vm.IsPaused);
    }

    [Fact]
    public async Task DisposePreventsACompletedLateScanFromUpdatingEntries()
    {
        var provider = new BlockedProvider();
        var vm = new PortMonitorViewModel(provider);
        vm.RefreshCommand.Execute(null);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        vm.Dispose();
        provider.Release.SetResult(Provider.Snapshot);
        await Task.Delay(80);
        Assert.Empty(vm.Entries);
        Assert.Equal("尚未刷新", vm.LastRefreshText);
        Assert.False(vm.RefreshCommand.CanExecute(null));
        Assert.False(vm.TogglePauseCommand.CanExecute(null));
    }

    [Fact]
    public async Task SwitchingFieldsOnlyFiltersTheExistingSnapshot()
    {
        var provider = new Provider();
        using var vm = new PortMonitorViewModel(provider);
        await vm.InitializeAsync();
        vm.RefreshCommand.Execute(null);
        await Until(() => provider.Calls >= 1 && !vm.IsRefreshing);
        vm.SearchText = "80";
        Assert.Equal(2, vm.Entries.Count); // All mode preserves numeric substring matching.
        var field = typeof(PortMonitorViewModel).GetProperty("SearchField");
        Assert.NotNull(field);
        field!.SetValue(vm, Enum.Parse(field.PropertyType, "Port"));
        Assert.Equal(80, Assert.Single(vm.Entries).LocalPort);
        field.SetValue(vm, Enum.Parse(field.PropertyType, "ProcessId"));
        Assert.Equal(80, Assert.Single(vm.Entries).ProcessId);
        field.SetValue(vm, Enum.Parse(field.PropertyType, "ProcessName"));
        Assert.Empty(vm.Entries);
        vm.SearchText = "editor";
        Assert.Equal("Example.Editor", Assert.Single(vm.Entries).ProcessName);
        field.SetValue(vm, Enum.Parse(field.PropertyType, "IpAddress"));
        vm.SearchText = "FE80";
        Assert.Equal("fe80::1%12", Assert.Single(vm.Entries).LocalAddress);
        Assert.Equal(1, provider.Calls);
    }

    private static async Task Until(Func<bool> predicate)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!predicate()) await Task.Delay(10, budget.Token);
    }

    private sealed class Provider : IPortSnapshotProvider
    {
        internal static readonly IReadOnlyList<PortEntry> Snapshot =
        [
            new("TCP", AddressFamily.InterNetwork, "127.0.0.1", 80, "198.51.100.1", 443, "Listen", 8080, "Example.Server"),
            new("UDP", AddressFamily.InterNetworkV6, "fe80::1%12", 8080, "", null, "Bound", 80, "Example.Editor")
        ];
        public int Calls;
        public Task<IReadOnlyList<PortEntry>> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(Snapshot);
        }
    }

    private sealed class BlockedProvider : IPortSnapshotProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IReadOnlyList<PortEntry>> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<PortEntry>> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return Release.Task; // Simulate a native provider that completes despite cancellation.
        }
    }
}
