using System.IO;
using ToolsBox.Core.ArchiveRecovery;
using ToolsBox.Windows.ArchiveRecovery;
using System.Diagnostics;
using System.Reflection;

namespace ToolsBox.App.Tests.ArchiveRecovery;

public sealed class ArchiveRecoveryClientTests
{
    [Fact]
    public async Task StartupTimeoutAndCancellation_ReapOwnedHelpers_AndCanRetry()
    {
        // This executable cannot speak our protocol. Its failure must never count as a bad password.
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        using var client = new ArchiveRecoveryClient(powershell);
        var watch = Stopwatch.StartNew();
        var result = await client.VerifyAsync(typeof(App).Assembly.Location, "secret-never-on-commandline", TimeSpan.FromSeconds(1), default);
        Assert.Equal(ArchivePasswordOutcome.Inconclusive, result.Outcome);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8));
        Assert.Null(typeof(ArchiveRecoveryClient).GetField("_worker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.VerifyAsync(typeof(App).Assembly.Location, "secret", TimeSpan.FromSeconds(30), cancel.Token));
        Assert.Null(typeof(ArchiveRecoveryClient).GetField("_worker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client));
    }

    [Fact]
    public async Task DisposeKillsConnectedHelperAndMalformedHandshakeIsRejected()
    {
        Assert.Equal(1, await ArchiveRecoveryWorker.RunAsync("bad-pipe", "bad-session", "1"));
        using var client = new ArchiveRecoveryClient(Path.Combine(AppContext.BaseDirectory, "宝哥工具箱.dll"));
        var result = await client.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "ArchiveRecovery", "Fixtures", "zip-aes.zip"), "test-pass", TimeSpan.FromSeconds(30), default);
        Assert.Equal(ArchivePasswordOutcome.Match, result.Outcome);
        var worker = (ArchiveWorkerProcess)typeof(ArchiveRecoveryClient).GetField("_worker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;
        using var observer = Process.GetProcessById(worker.Id);
        Assert.Equal(IntPtr.Zero, observer.MainWindowHandle);
        client.Dispose();
        Assert.True(observer.WaitForExit(5000));
    }
    [Theory]
    [InlineData("zip-aes.zip", "test-pass")]
    [InlineData("7z-unicode-spaces.7z", " Aa密码1! ")]
    [InlineData("zip-crypto.zip", "test-pass")]
    [InlineData("7z-data.7z", "test-pass")]
    [InlineData("7z-headers.7z", "test-pass")]
    [InlineData("Rar.encrypted_filesOnly.rar", "test")]
    [InlineData("Rar.encrypted_filesAndHeader.rar", "test")]
    [InlineData("Rar5.encrypted_filesOnly.rar", "test")]
    [InlineData("Rar5.encrypted_filesAndHeader.rar", "test")]
    public async Task RealWorker_WrongThenCorrect_ReusesHeadlessWorkerAndCancels(string file, string password)
    {
        Type? type = typeof(ToolsBox.Windows.FileUnlocking.WindowsFileLockService).Assembly
            .GetType("ToolsBox.Windows.ArchiveRecovery.ArchiveRecoveryClient");
        Assert.NotNull(type);
        using var owner = (IDisposable)Activator.CreateInstance(type!, Path.Combine(AppContext.BaseDirectory, "宝哥工具箱.dll"))!;
        var client = (IArchivePasswordVerifier)owner;
        string sample = Path.Combine(AppContext.BaseDirectory, "ArchiveRecovery", "Fixtures", file);
        Assert.True(File.Exists(sample));
        var wrong = await client.VerifyAsync(sample, "wrong", TimeSpan.FromSeconds(30), default);
        Assert.Contains(wrong.Outcome, new[] { ArchivePasswordOutcome.NoMatch, ArchivePasswordOutcome.RejectedUncertain });
        var right = await client.VerifyAsync(sample, password, TimeSpan.FromSeconds(30), default);
        Assert.Equal(ArchivePasswordOutcome.Match, right.Outcome);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.VerifyAsync(sample, "test-pass", TimeSpan.FromSeconds(30), cancel.Token));
    }
}
