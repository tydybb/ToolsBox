namespace ToolsBox.Core.FileUnlocking;

public interface IFileLockService
{
    Task<IReadOnlyList<FileLockEntry>> FindLocksAsync(FileLockTarget target, CancellationToken cancellationToken = default);
    Task<FileUnlockResult> CloseHandleAsync(FileLockEntry entry, CancellationToken cancellationToken = default);
    Task<FileUnlockResult> TerminateProcessAsync(FileLockEntry entry, CancellationToken cancellationToken = default);
}
