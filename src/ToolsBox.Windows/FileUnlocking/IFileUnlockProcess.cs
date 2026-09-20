using System.Diagnostics;

namespace ToolsBox.Windows.FileUnlocking;

internal interface IFileUnlockProcess : IDisposable
{
    int Id { get; }
    string ProcessName { get; }
    DateTime StartTime { get; }
    bool HasExited { get; }
    void Kill(bool entireProcessTree);
    bool WaitForExit(int milliseconds);
}

internal sealed class FileUnlockProcess : IFileUnlockProcess
{
    private readonly Process _process;

    public FileUnlockProcess(Process process)
    {
        _process = process;
        try
        {
            // Process owns this cached handle until Dispose. Pin it before identity
            // checks so StartTime, Kill and WaitForExit cannot reopen a reused PID.
            _ = process.SafeHandle;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public int Id => _process.Id;
    public string ProcessName => _process.ProcessName;
    public DateTime StartTime => _process.StartTime;
    public bool HasExited => _process.HasExited;
    public void Kill(bool entireProcessTree) => _process.Kill(entireProcessTree);
    public bool WaitForExit(int milliseconds) => _process.WaitForExit(milliseconds);
    public void Dispose() => _process.Dispose();
}
