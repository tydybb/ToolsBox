using System.Collections.ObjectModel;

namespace ToolsBox.Core.FileUnlocking;

public sealed class FileLockScanResult(IList<FileLockEntry> entries, int skippedHandleCount)
    : ReadOnlyCollection<FileLockEntry>(entries)
{
    public int SkippedHandleCount { get; } = skippedHandleCount;
}
