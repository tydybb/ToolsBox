using ToolsBox.Core.NetworkTraffic;

namespace ToolsBox.Core.Tests.NetworkTraffic;

public sealed class NetworkTrafficFilterTests
{
    [Theory]
    [InlineData("browser")]
    [InlineData("C:\\Apps\\Browser.exe")]
    [InlineData("123")]
    public void Apply_MatchesNamePathOrPid(string search)
    {
        ApplicationTrafficSnapshot browser = CreateSnapshot(
            "BROWSER",
            "Browser",
            @"C:\Apps\Browser.exe",
            10,
            20,
            123);
        ApplicationTrafficSnapshot editor = CreateSnapshot(
            "EDITOR",
            "Editor",
            @"C:\Apps\Editor.exe",
            30,
            40,
            456);

        ApplicationTrafficSnapshot result = Assert.Single(NetworkTrafficFilter.Apply([browser, editor], search, false));

        Assert.Same(browser, result);
    }

    [Fact]
    public void Apply_ActiveOnlyRemovesZeroRateRowsAndSortsByCombinedRate()
    {
        ApplicationTrafficSnapshot slow = CreateSnapshot("SLOW", "Slow", @"C:\Slow.exe", 10, 5, 1);
        ApplicationTrafficSnapshot idle = CreateSnapshot("IDLE", "Idle", @"C:\Idle.exe", 0, 0, 2);
        ApplicationTrafficSnapshot fast = CreateSnapshot("FAST", "Fast", @"C:\Fast.exe", 50, 100, 3);

        IReadOnlyList<ApplicationTrafficSnapshot> result = NetworkTrafficFilter.Apply([slow, idle, fast], string.Empty, true);

        Assert.Equal([fast, slow], result);
    }

    private static ApplicationTrafficSnapshot CreateSnapshot(
        string key,
        string name,
        string path,
        long uploadRate,
        long downloadRate,
        int pid)
    {
        var process = new ProcessTrafficSnapshot(
            new ProcessIdentity(pid, DateTimeOffset.UnixEpoch),
            name,
            path,
            true,
            false,
            uploadRate,
            downloadRate,
            uploadRate,
            downloadRate);
        return new ApplicationTrafficSnapshot(
            key,
            name,
            path,
            true,
            uploadRate,
            downloadRate,
            uploadRate,
            downloadRate,
            [process]);
    }
}
