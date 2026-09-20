using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ToolsBox.App.FileUnlocking;
using ToolsBox.Core.FileUnlocking;

namespace ToolsBox.App.Tests.Startup;

public sealed class ProcessTerminationSafetyTests
{
    [Fact]
    public async Task Confirmation_FreezesSelectedDistinctTargets_AndExecutionUsesExactlyThatSnapshot()
    {
        var service = new RecordingService();
        using var vm = CreateModel(service);
        var first = Entry(101, 1);
        vm.Entries.Add(first);
        vm.Entries.Add(Entry(101, 2));
        vm.Entries.Add(Entry(102, 3) with { IsSelected = false });
        vm.Entries.Add(Entry(103, 4));
        bool prompted = false;

        await vm.TerminateSelectedProcessesAsync(targets =>
        {
            prompted = true;
            Assert.True(vm.IsBusy);
            Assert.False(vm.CanSelectEntries);
            Assert.Equal(new[] { 101, 103 }, targets.Select(item => item.ProcessId));
            Assert.NotSame(first, targets[0]);
            Assert.Throws<NotSupportedException>(() => ((IList<FileLockEntry>)targets)[0] = Entry(999, 9));
            vm.Entries.Clear();
            vm.Entries.Add(Entry(999, 9));
            return true;
        });

        Assert.True(prompted);
        Assert.Equal(new[] { 101, 103 }, service.Terminated.Select(item => item.ProcessId));
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Cancel_DoesNotTerminateOrScan_AndReleasesBusyState()
    {
        var service = new RecordingService();
        using var vm = CreateModel(service);
        vm.Entries.Add(Entry(101, 1));
        string result = await vm.TerminateSelectedProcessesAsync(_ => false);
        Assert.Contains("取消", result);
        Assert.Empty(service.Terminated);
        Assert.Equal(0, service.ScanCalls);
        Assert.True(vm.Entries[0].IsSelected);
        Assert.False(vm.IsBusy);
    }

    [Theory]
    [InlineData("explorer.exe")]
    [InlineData("svchost")]
    public async Task ProtectedTarget_BlocksWholeBatchBeforeConfirmation(string name)
    {
        var service = new RecordingService();
        using var vm = CreateModel(service);
        vm.Entries.Add(Entry(101, 1));
        vm.Entries.Add(Entry(102, 2) with { ProcessName = name });
        bool prompted = false;
        string result = await vm.TerminateSelectedProcessesAsync(_ => { prompted = true; return true; });
        Assert.False(prompted);
        Assert.Empty(service.Terminated);
        Assert.Contains(name, result);
        Assert.Contains("取消勾选", result);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ProtectedDuplicateRecord_IsNotDiscardedBeforeBatchSafetyCheck()
    {
        var service = new RecordingService();
        using var vm = CreateModel(service);
        vm.Entries.Add(Entry(101, 1));
        vm.Entries.Add(Entry(101, 2) with { ProcessName = "explorer" });
        vm.Entries.Add(Entry(103, 3));
        string result = await vm.TerminateSelectedProcessesAsync(_ => true);
        Assert.Empty(service.Terminated);
        Assert.Contains("explorer", result);
    }

    [Fact]
    public async Task ConflictingProcessIdentities_RequireRescanBeforeAnyTermination()
    {
        var service = new RecordingService();
        using var vm = CreateModel(service);
        vm.Entries.Add(Entry(101, 1));
        vm.Entries.Add(Entry(101, 2) with { ProcessStartedAt = DateTimeOffset.MaxValue });
        vm.Entries.Add(Entry(103, 3));
        string result = await vm.TerminateSelectedProcessesAsync(_ => true);
        Assert.Empty(service.Terminated);
        Assert.Contains("重新扫描", result);
    }

    [Fact]
    public async Task Confirmation_BlocksInputScanningAndReentrantActions()
    {
        var service = new RecordingService();
        using var vm = CreateModel(service);
        string path = vm.PathText;
        vm.Entries.Add(Entry(101, 1));
        var attempts = new List<Task>();
        bool prompted = false;
        await vm.TerminateSelectedProcessesAsync(_ =>
        {
            prompted = true;
            Assert.False(vm.ScanCommand.CanExecute(null));
            Assert.False(vm.ElevatedScanCommand.CanExecute(null));
            vm.PathText = "changed";
            Assert.Equal(path, vm.PathText);
            attempts.Add(vm.ScanAsync());
            attempts.Add(vm.SetPathAndScanAsync("changed"));
            attempts.Add(vm.TerminateSelectedProcessesAsync(_ => true));
            attempts.Add(vm.CloseSelectedHandlesAsync());
            Assert.All(attempts, task => Assert.True(task.IsCompletedSuccessfully));
            Assert.Equal(0, service.ScanCalls);
            Assert.Empty(service.Terminated);
            Assert.Equal(0, service.CloseCalls);
            return false;
        });
        await Task.WhenAll(attempts);
        Assert.True(prompted);
        Assert.Equal(path, vm.PathText);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void ChangingPath_ClearsOldSelectionAndResults()
    {
        using var vm = CreateModel(new RecordingService());
        vm.Entries.Add(Entry(101, 1));
        vm.PathText = "another folder";
        Assert.Empty(vm.Entries);
        Assert.Equal(0, vm.SelectedCount);
        Assert.False(vm.CanActOnSelectedEntries);
    }

    [Fact]
    public async Task DisposedDuringConfirmation_DoesNotExecute()
    {
        var service = new RecordingService();
        using var vm = CreateModel(service);
        vm.Entries.Add(Entry(101, 1));
        await vm.TerminateSelectedProcessesAsync(_ => { vm.Dispose(); return true; });
        Assert.Empty(service.Terminated);
    }

    [Fact]
    public async Task FailedConfirmation_ReleasesBusyStateWithoutExecuting()
    {
        var service = new RecordingService();
        using var vm = CreateModel(service);
        vm.Entries.Add(Entry(101, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.TerminateSelectedProcessesAsync(_ => throw new InvalidOperationException("dialog failed")));
        Assert.False(vm.IsBusy);
        Assert.Empty(service.Terminated);
    }

    [Fact]
    public Task ConfirmationWindow_ShowsFullList_DefaultsToCancel_RequiresAcknowledgement() => WpfTestThread.RunAsync(async () =>
    {
        Type? type = typeof(App).Assembly.GetType("ToolsBox.App.Views.ProcessTerminationDialog");
        Assert.NotNull(type);
        var entries = Enumerable.Range(1, 60).Select(id => Entry(id + 100, id)).ToArray();
        var window = Assert.IsAssignableFrom<Window>(Activator.CreateInstance(type!, new object[] { @"E:\work\example", entries }));
        try
        {
            window.Measure(new Size(880, 580));
            window.Arrange(new Rect(0, 0, 880, 580));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var list = Assert.IsType<DataGrid>(window.FindName("ProcessList"));
            Assert.Equal(60, list.Items.Count);
            Assert.Equal(entries[59], list.Items[59]);
            Assert.Equal(new[] { "进程", "PID", "程序路径" }, list.Columns.Select(column => column.Header));
            Assert.Equal(@"E:\work\example", Assert.IsType<TextBlock>(window.FindName("TargetPathText")).Text);
            var confirm = Assert.IsType<Button>(window.FindName("ConfirmTerminationButton"));
            var cancel = Assert.IsType<Button>(window.FindName("CancelButton"));
            var acknowledgement = Assert.IsType<CheckBox>(window.FindName("RiskAcknowledgement"));
            Assert.True(cancel.IsDefault);
            Assert.True(cancel.IsCancel);
            Assert.False(confirm.IsDefault);
            Assert.False(confirm.IsEnabled);
            Assert.NotEqual(true, acknowledgement.IsChecked);
            WpfTestSnapshot.SaveWindowContent(window, 880, 540, "process-termination-confirmation.png");
            Assert.True(list.Columns[0].ActualWidth >= 180, $"Process name width: {list.Columns[0].ActualWidth}");
            Assert.True(list.Columns[1].ActualWidth >= 90, $"PID width: {list.Columns[1].ActualWidth}");
            acknowledgement.IsChecked = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(confirm.IsEnabled);
        }
        finally { window.Close(); }
    });

    private static FileUnlockerViewModel CreateModel(RecordingService service) => new(service) { PathText = Path.GetTempPath() };
    private static FileLockEntry Entry(int pid, int handle) =>
        new(@"E:\work\example\held.txt", handle, pid, $"holder-{pid}", $"C:\\apps\\holder-{pid}.exe", new DateTimeOffset(2026, 9, 18, 8, 0, 0, TimeSpan.Zero), false) { IsSelected = true };

    private sealed class RecordingService : IFileLockService
    {
        public List<FileLockEntry> Terminated { get; } = [];
        public int ScanCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public Task<IReadOnlyList<FileLockEntry>> FindLocksAsync(FileLockTarget target, CancellationToken cancellationToken = default)
        { ScanCalls++; return Task.FromResult<IReadOnlyList<FileLockEntry>>([]); }
        public Task<IReadOnlyList<FileLockEntry>> FindLocksElevatedAsync(FileLockTarget target, CancellationToken cancellationToken = default) => FindLocksAsync(target, cancellationToken);
        public Task<FileUnlockResult> CloseHandleAsync(FileLockEntry entry, CancellationToken cancellationToken = default)
        { CloseCalls++; return Task.FromResult(FileUnlockResult.Success("fake only")); }
        public Task<FileUnlockResult> TerminateProcessAsync(FileLockEntry entry, CancellationToken cancellationToken = default)
        { Terminated.Add(entry); return Task.FromResult(FileUnlockResult.Success("fake only")); }
    }
}
