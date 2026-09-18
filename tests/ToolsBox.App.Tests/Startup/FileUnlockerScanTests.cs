using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ToolsBox.App.FileUnlocking;
using ToolsBox.App.Views;
using ToolsBox.Windows.FileUnlocking;
using ToolsBox.Core.FileUnlocking;

namespace ToolsBox.App.Tests.Startup;

public sealed class FileUnlockerScanTests
{
    [Fact]
    public Task FolderScan_CompletesAndReenablesBothButtons() => WpfTestThread.RunAsync(async () =>
    {
        DirectoryInfo folder = Directory.CreateTempSubdirectory("ToolsBox-scan-");
        string path = Path.Combine(folder.FullName, "held.txt");
        try
        {
            using var held = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
            using var vm = new FileUnlockerViewModel(new WindowsFileLockService(
                Environment.GetEnvironmentVariable("TOOLSBOX_TEST_WORKER") ?? Path.Combine(AppContext.BaseDirectory, "宝哥工具箱.dll")));
            var view = new FileUnlockerView { DataContext = vm };
            view.Measure(new Size(1200, 800));
            view.Arrange(new Rect(0, 0, 1200, 800));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Button[] buttons = Descendants(view).OfType<Button>()
                .Where(button => button.Content is "检测占用" or "管理员扫描").ToArray();
            Assert.Equal(2, buttons.Length);
            Assert.All(buttons, button => Assert.False(button.IsEnabled));
            await vm.HandleDroppedPathsAsync([folder.FullName]).WaitAsync(TimeSpan.FromSeconds(10));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(vm.IsBusy);
            Assert.Contains(vm.Entries, entry => entry.ProcessId == Environment.ProcessId && entry.LockedPath == path);
            Assert.All(buttons, button => Assert.True(button.IsEnabled));
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.ScanCommand.CanExecuteChanged += (_, _) =>
            {
                if (!vm.IsBusy && vm.ScanCommand.CanExecute(null)) completed.TrySetResult();
            };
            vm.ScanCommand.Execute(null);
            Assert.All(buttons, button => Assert.False(button.IsEnabled));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Contains(vm.Entries, entry => entry.ProcessId == Environment.ProcessId && entry.LockedPath == path);
            Assert.All(buttons, button => Assert.True(button.IsEnabled));
        }
        finally
        {
            File.Delete(path);
            folder.Delete();
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CompletedOrFailedScan_ReleasesCommandsAndReportsIncompleteResults(bool fail) =>
        WpfTestThread.RunAsync(async () =>
        {
            var service = new PendingScanService();
            using var vm = new FileUnlockerViewModel(service) { PathText = Path.GetTempPath() };
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.ElevatedScanCommand.CanExecuteChanged += (_, _) =>
            {
                if (!vm.IsBusy && vm.ElevatedScanCommand.CanExecute(null)) completed.TrySetResult();
            };
            vm.ElevatedScanCommand.Execute(null);
            Assert.True(vm.IsBusy);
            Assert.False(vm.ScanCommand.CanExecute(null));
            Assert.False(vm.ElevatedScanCommand.CanExecute(null));
            if (fail) service.Result.SetException(new TimeoutException("扫描超过 30 秒"));
            else service.Result.SetResult(new FileLockScanResult([], 2));
            await completed.Task;
            Assert.False(vm.IsBusy);
            Assert.True(vm.ScanCommand.CanExecute(null));
            Assert.True(vm.ElevatedScanCommand.CanExecute(null));
            Assert.Contains(fail ? "超过 30 秒" : "结果可能不完整", vm.StatusText);
            Assert.DoesNotContain("可正常操作", vm.StatusText);
        });

    private sealed class PendingScanService : IFileLockService
    {
        public TaskCompletionSource<IReadOnlyList<FileLockEntry>> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<FileLockEntry>> FindLocksAsync(FileLockTarget target, CancellationToken cancellationToken = default) => Result.Task;
        public Task<IReadOnlyList<FileLockEntry>> FindLocksElevatedAsync(FileLockTarget target, CancellationToken cancellationToken = default) => Result.Task;
        public Task<FileUnlockResult> CloseHandleAsync(FileLockEntry entry, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FileUnlockResult> TerminateProcessAsync(FileLockEntry entry, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }
}
