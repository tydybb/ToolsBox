using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;
using ToolsBox.App.FileUnlocking;
using ToolsBox.App.Infrastructure;
using ToolsBox.Core.FileUnlocking;

namespace ToolsBox.App.Tests.Startup;

public sealed class ShellFileDropReceiverTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task NativeDrop_RecognizesPathAndScansWithoutUnlocking(bool isDirectory) =>
        WpfTestThread.RunAsync(async () =>
        {
            DirectoryInfo folder = Directory.CreateTempSubdirectory("ToolsBox-drop-中文 ");
            string file = Path.Combine(folder.FullName, "测试 file.txt");
            File.WriteAllText(file, "fixture");
            try
            {
                using var source = new HwndSource(new HwndSourceParameters("ToolsBox drop test")
                    { Width = 10, Height = 10, WindowStyle = unchecked((int)0x80000000) });
                var service = new RecordingLockService();
                using var vm = new FileUnlockerViewModel(service);
                Task? scan = null;
                using var receiver = new ShellFileDropReceiver(source, paths => scan = vm.HandleDroppedPathsAsync(paths));
                receiver.SetEnabled(true);
                string path = isDirectory ? folder.FullName : file;
                SendDrop(source.Handle, [path]);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.NotNull(scan);
                await scan;
                Assert.Equal(path, vm.PathText);
                Assert.Equal(FileLockTarget.FromExistingPath(path), Assert.Single(service.Targets));
                Assert.Equal(isDirectory, service.Targets[0].IsDirectory);

                // Disabled pages must consume/release the native payload but not start a scan.
                receiver.SetEnabled(false);
                SendDrop(source.Handle, [file]);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Single(service.Targets);

                receiver.SetEnabled(true);
                SendDrop(source.Handle, [folder.FullName, file]);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Single(service.Targets);
                Assert.Equal(path, vm.PathText);
                Assert.Contains("一次拖入一个", vm.StatusText);

                SendDrop(source.Handle, [Path.Combine(folder.FullName, "missing")]);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Single(service.Targets);
                Assert.Contains("不存在", vm.StatusText);
                receiver.Dispose();
                receiver.Dispose();
            }
            finally
            {
                File.Delete(file);
                folder.Delete();
            }
        });

    private static void SendDrop(IntPtr hwnd, string[] paths)
    {
        byte[] data = Encoding.Unicode.GetBytes(string.Join('\0', paths) + "\0\0");
        IntPtr drop = GlobalAlloc(0x42 /* GMEM_MOVEABLE | GMEM_ZEROINIT */, (UIntPtr)(20 + data.Length));
        Assert.NotEqual(IntPtr.Zero, drop);
        IntPtr buffer = GlobalLock(drop);
        Assert.NotEqual(IntPtr.Zero, buffer);
        Marshal.WriteInt32(buffer, 0, 20); // DROPFILES.pFiles
        Marshal.WriteInt32(buffer, 16, 1); // DROPFILES.fWide
        Marshal.Copy(data, 0, buffer + 20, data.Length);
        GlobalUnlock(drop);
        SendMessage(hwnd, 0x233, drop, IntPtr.Zero); // Receiver owns DragFinish.
        Assert.Equal(0x8000u /* GMEM_INVALID_HANDLE */, GlobalFlags(drop));
    }

    private sealed class RecordingLockService : IFileLockService
    {
        public List<FileLockTarget> Targets { get; } = [];
        public Task<IReadOnlyList<FileLockEntry>> FindLocksAsync(FileLockTarget target, CancellationToken cancellationToken = default)
        {
            Targets.Add(target);
            return Task.FromResult<IReadOnlyList<FileLockEntry>>([]);
        }
        public Task<IReadOnlyList<FileLockEntry>> FindLocksElevatedAsync(FileLockTarget target, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Dropping must not launch another elevated process.");
        public Task<FileUnlockResult> CloseHandleAsync(FileLockEntry entry, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Dropping must never close handles.");
        public Task<FileUnlockResult> TerminateProcessAsync(FileLockEntry entry, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Dropping must never terminate processes.");
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern uint GlobalFlags(IntPtr memory);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}
