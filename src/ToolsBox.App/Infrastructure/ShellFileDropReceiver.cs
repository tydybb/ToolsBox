using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace ToolsBox.App.Infrastructure;

/// <summary>Receives shell paths on an elevated window without using cross-integrity OLE drag/drop.</summary>
public sealed class ShellFileDropReceiver : IDisposable
{
    private const int WmDropFiles = 0x0233;
    private const uint WmCopyGlobalData = 0x0049; // Shell's transfer of the HDROP memory block.
    private readonly HwndSource _source;
    private readonly Action<string[]> _receive;
    private bool _enabled;
    private bool _disposed;

    public ShellFileDropReceiver(HwndSource source, Action<string[]> receive)
    {
        _source = source;
        _receive = receive;
        // HwndSource registers an OLE target even if no element has AllowDrop=true.
        // Explorer must use the shell fallback instead when the target is elevated.
        int result = RevokeDragDrop(source.Handle);
        if (result < 0 && result != unchecked((int)0x80040100)) // DRAGDROP_E_NOTREGISTERED
            Marshal.ThrowExceptionForHR(result);
        _source.AddHook(WindowProc);
    }

    public void SetEnabled(bool enabled)
    {
        if (_disposed || _source.IsDisposed || enabled == _enabled) return;
        uint action = enabled ? 1u /* MSGFLT_ALLOW */ : 2u /* MSGFLT_DISALLOW */;
        if (!ChangeWindowMessageFilterEx(_source.Handle, WmDropFiles, action, IntPtr.Zero) ||
            !ChangeWindowMessageFilterEx(_source.Handle, WmCopyGlobalData, action, IntPtr.Zero))
        {
            int error = Marshal.GetLastWin32Error();
            ChangeWindowMessageFilterEx(_source.Handle, WmDropFiles, 2, IntPtr.Zero);
            ChangeWindowMessageFilterEx(_source.Handle, WmCopyGlobalData, 2, IntPtr.Zero);
            DragAcceptFiles(_source.Handle, false);
            _enabled = false;
            throw new Win32Exception(error);
        }
        DragAcceptFiles(_source.Handle, enabled);
        _enabled = enabled;
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmDropFiles) return IntPtr.Zero;
        handled = true;
        string[] paths = [];
        try
        {
            // Only one existing filesystem path is accepted downstream. Never execute dropped data.
            if (_enabled && DragQueryFile(wParam, uint.MaxValue, null, 0) == 1)
            {
                uint length = DragQueryFile(wParam, 0, null, 0);
                if (length is > 0 and < 32768)
                {
                    var buffer = new StringBuilder((int)length + 1);
                    if (DragQueryFile(wParam, 0, buffer, (uint)buffer.Capacity) == length)
                        paths = [buffer.ToString()];
                }
            }
        }
        finally
        {
            DragFinish(wParam);
        }

        if (_enabled)
        {
            _source.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_disposed && _enabled) _receive(paths);
            }));
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _enabled = false;
        if (!_source.IsDisposed)
        {
            DragAcceptFiles(_source.Handle, false);
            ChangeWindowMessageFilterEx(_source.Handle, WmDropFiles, 2, IntPtr.Zero);
            ChangeWindowMessageFilterEx(_source.Handle, WmCopyGlobalData, 2, IntPtr.Zero);
            _source.RemoveHook(WindowProc);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr changeInfo);
    [DllImport("shell32.dll")]
    private static extern void DragAcceptFiles(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool accept);
    [DllImport("shell32.dll", EntryPoint = "DragQueryFileW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder? path, uint size);
    [DllImport("shell32.dll")]
    private static extern void DragFinish(IntPtr drop);
}
