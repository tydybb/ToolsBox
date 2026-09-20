using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using ToolsBox.App.FileUnlocking;
using ToolsBox.App.Views;
using ToolsBox.Core.FileUnlocking;

namespace ToolsBox.App.Tests.Startup;

public sealed class FileUnlockerSelectionTests
{
    [Fact]
    public void Entry_NotifiesOnlyChanges_AndPreservesRecordAndJsonSemantics()
    {
        FileLockEntry entry = Entry(1);
        var notifications = new List<string?>();
        INotifyPropertyChanged observable = Assert.IsAssignableFrom<INotifyPropertyChanged>(entry);
        observable.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        Assert.Equal(Entry(1), entry);
        Assert.Equal(Entry(1).GetHashCode(), entry.GetHashCode());
        entry.IsSelected = true;
        entry.IsSelected = true;
        Assert.Equal([nameof(FileLockEntry.IsSelected)], notifications);
        Assert.NotEqual(Entry(1), entry);
        FileLockEntry copy = entry with { };
        Assert.Equal(entry, copy);
        copy.IsSelected = false;
        Assert.Single(notifications);
        Assert.Equal(entry, JsonSerializer.Deserialize<FileLockEntry>(JsonSerializer.Serialize(entry)));
    }

    [Fact]
    public void Selection_TracksNonePartialAll_AndCountsDistinctProcesses()
    {
        using var vm = new FileUnlockerViewModel(new SelectionService());
        vm.Entries.Add(Entry(1, 10));
        vm.Entries.Add(Entry(2, 10));
        vm.Entries.Add(Entry(3, 20));
        AssertSelection(vm, false, 0, 0);
        vm.Entries[0].IsSelected = true;
        AssertSelection(vm, null, 1, 1);
        var changes = new List<string?>();
        vm.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        vm.ToggleSelectAllCommand.Execute(null);
        Assert.All(vm.Entries, row => Assert.True(row.IsSelected));
        AssertSelection(vm, true, 3, 2);
        Assert.Single(changes.Where(name => name == nameof(vm.SelectedCount)));
        vm.ToggleSelectAllCommand.Execute(null);
        AssertSelection(vm, false, 0, 0);
    }

    [Fact]
    public void CollectionChanges_ResetAndDispose_DetachOldRows()
    {
        var vm = new FileUnlockerViewModel(new SelectionService());
        FileLockEntry first = Entry(1);
        vm.Entries.Add(first);
        first.IsSelected = true;
        vm.Entries[0] = Entry(2);
        AssertSelection(vm, false, 0, 0);
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        first.IsSelected = false;
        Assert.Empty(notifications);
        FileLockEntry old = vm.Entries[0];
        vm.Entries.Clear();
        notifications.Clear();
        old.IsSelected = true;
        Assert.Empty(notifications);
        FileLockEntry current = Entry(3);
        vm.Entries.Add(current);
        vm.Dispose();
        notifications.Clear();
        current.IsSelected = true;
        vm.Entries.Add(Entry(4));
        Assert.Empty(notifications);
        Assert.False(vm.ToggleSelectAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task EmptyAndPendingScan_DisableSelection_AndFreshResultsAreUnselected()
    {
        var service = new SelectionService { ScanResult = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var vm = new FileUnlockerViewModel(service) { PathText = Path.GetTempPath() };
        AssertSelection(vm, false, 0, 0);
        Assert.False(vm.CanSelectEntries);
        Assert.False(vm.CanActOnSelectedEntries);
        Assert.False(vm.ToggleSelectAllCommand.CanExecute(null));
        vm.Entries.Add(Entry(1));
        Task scan = vm.ScanAsync();
        Assert.True(vm.IsBusy);
        Assert.False(vm.CanSelectEntries);
        Assert.False(vm.CanActOnSelectedEntries);
        Assert.False(vm.ToggleSelectAllCommand.CanExecute(null));
        vm.ToggleSelectAllCommand.Execute(null);
        Assert.False(vm.Entries[0].IsSelected);
        FileLockEntry fresh = Entry(2) with { IsSelected = true };
        service.ScanResult.SetResult([fresh]);
        await scan;
        Assert.True(vm.CanSelectEntries);
        AssertSelection(vm, false, 0, 0);
        Assert.False(fresh.IsSelected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingAction_DisablesSelection_AndTerminationDeduplicatesProcess(bool terminate)
    {
        var service = new SelectionService { ActionResult = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var vm = new FileUnlockerViewModel(service) { PathText = Path.GetTempPath() };
        vm.Entries.Add(Entry(1, 10) with { IsSelected = true });
        vm.Entries.Add(Entry(2, 10) with { IsSelected = true });
        Task<string> action = terminate ? vm.TerminateSelectedProcessesAsync(_ => true) : vm.CloseSelectedHandlesAsync();
        Assert.True(vm.IsBusy);
        Assert.False(vm.CanSelectEntries);
        Assert.False(vm.CanActOnSelectedEntries);
        Assert.False(vm.ToggleSelectAllCommand.CanExecute(null));
        vm.ToggleSelectAllCommand.Execute(null);
        Assert.All(vm.Entries, row => Assert.True(row.IsSelected));
        await vm.TerminateSelectedProcessesAsync(_ => true);
        await vm.CloseSelectedHandlesAsync();
        Assert.Equal(1, service.ActionCalls);
        service.ActionResult.SetResult(FileUnlockResult.Success("ok"));
        await action;
        Assert.Equal(terminate ? 1 : 2, service.ActionCalls);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public Task ActualHeaderAndRows_ToggleWholeCollection_AndDisableOnlySelectionWhileBusy() =>
        WpfTestThread.RunAsync(async () =>
        {
            var service = new SelectionService { ScanResult = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            using var vm = new FileUnlockerViewModel(service) { PathText = Path.GetTempPath() };
            foreach (int index in Enumerable.Range(0, 100)) vm.Entries.Add(Entry(index));
            var view = new FileUnlockerView { DataContext = vm };
            view.Measure(new Size(1200, 600));
            view.Arrange(new Rect(0, 0, 1200, 600));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            CheckBox header = Assert.Single(Descendants(view).OfType<CheckBox>().Where(box => Equals(box.Content, "全选")));
            CheckBox row = Descendants(view).OfType<CheckBox>().First(box => box.DataContext is FileLockEntry);
            Button[] browse = Descendants(view).OfType<Button>().Where(button => button.Content is "选择文件" or "选择文件夹").ToArray();
            Assert.Equal(2, browse.Length);
            Assert.Contains(Descendants(view).OfType<Button>(), button => Equals(button.Content, "强制结束所选进程"));
            Assert.DoesNotContain(Descendants(view).OfType<Button>(), button => Equals(button.Content, "安全结束所选进程"));
            Assert.True(Descendants(view).OfType<DataGridRow>().Count() < vm.Entries.Count);
            Click(row);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Null(header.IsChecked);
            Click(header);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(header.IsChecked);
            Assert.All(vm.Entries, entry => Assert.True(entry.IsSelected));
            Click(header);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(header.IsChecked);
            Assert.All(vm.Entries, entry => Assert.False(entry.IsSelected));
            Task scan = vm.ScanAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(header.IsEnabled);
            Assert.False(row.IsEnabled);
            Assert.All(browse, button => Assert.False(button.IsEnabled));
            Assert.True(Assert.Single(Descendants(view).OfType<DataGrid>()).IsEnabled);
            service.ScanResult.SetResult([]);
            await scan;
        });

    private static void Click(CheckBox box) => typeof(ToggleButton)
        .GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(box, null);

    private static void AssertSelection(FileUnlockerViewModel vm, bool? all, int count, int processes)
    {
        Assert.Equal(all, vm.AllSelected);
        Assert.Equal(count, vm.SelectedCount);
        Assert.Equal(processes, vm.SelectedProcessCount);
    }

    private static FileLockEntry Entry(int handle, int pid = 1) => new("held.txt", handle, pid, "holder", "holder.exe", new DateTimeOffset(2026, 9, 18, 8, 0, 0, TimeSpan.Zero), false);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class SelectionService : IFileLockService
    {
        public TaskCompletionSource<IReadOnlyList<FileLockEntry>>? ScanResult { get; init; }
        public TaskCompletionSource<FileUnlockResult>? ActionResult { get; init; }
        public int ActionCalls { get; private set; }
        public Task<IReadOnlyList<FileLockEntry>> FindLocksAsync(FileLockTarget target, CancellationToken cancellationToken = default) => ScanResult?.Task ?? Task.FromResult<IReadOnlyList<FileLockEntry>>([]);
        public Task<IReadOnlyList<FileLockEntry>> FindLocksElevatedAsync(FileLockTarget target, CancellationToken cancellationToken = default) => FindLocksAsync(target, cancellationToken);
        public Task<FileUnlockResult> CloseHandleAsync(FileLockEntry entry, CancellationToken cancellationToken = default)
        {
            ActionCalls++;
            return ActionResult?.Task ?? Task.FromResult(FileUnlockResult.Success("ok"));
        }
        public Task<FileUnlockResult> TerminateProcessAsync(FileLockEntry entry, CancellationToken cancellationToken = default) => CloseHandleAsync(entry, cancellationToken);
    }
}
