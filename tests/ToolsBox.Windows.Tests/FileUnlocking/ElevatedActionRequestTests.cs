using ToolsBox.Core.FileUnlocking;
using ToolsBox.Windows.FileUnlocking;

namespace ToolsBox.Windows.Tests.FileUnlocking;

public sealed class ElevatedActionRequestTests
{
    [Fact]
    public void EncodeAndDecode_RoundTripsAllValidationFields()
    {
        var entry = new FileLockEntry(@"C:\Temp\数据.txt", 0x42, 123, "notepad", @"C:\Windows\notepad.exe",
            new DateTimeOffset(2026, 9, 17, 1, 2, 3, TimeSpan.Zero), true);
        var request = new ElevatedActionRequest("close-handle", entry);

        ElevatedActionRequest decoded = ElevatedActionRequest.Decode(request.Encode());

        Assert.Equal(request.Action, decoded.Action);
        FileLockEntry decodedEntry = Assert.IsType<FileLockEntry>(decoded.Entry);
        Assert.Equal(entry.LockedPath, decodedEntry.LockedPath);
        Assert.Equal(entry.HandleValue, decodedEntry.HandleValue);
        Assert.Equal(entry.ProcessId, decodedEntry.ProcessId);
        Assert.Equal(entry.ProcessStartedAt, decodedEntry.ProcessStartedAt);
    }
}
