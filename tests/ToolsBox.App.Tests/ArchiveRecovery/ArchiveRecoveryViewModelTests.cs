using System.Reflection;
using System.IO;
using System.Windows.Threading;
using ToolsBox.App.ArchiveRecovery;
using ToolsBox.Core.ArchiveRecovery;

namespace ToolsBox.App.Tests.ArchiveRecovery;

public sealed class ArchiveRecoveryViewModelTests
{
    [Fact]
    public Task DroppedArchive_IsIdentifiedWithoutStartingRecovery() => WpfTestThread.RunAsync(async () =>
    {
        using var vm = new ArchiveRecoveryViewModel(() => throw new InvalidOperationException("must not start"));
        string sample = Path.Combine(AppContext.BaseDirectory, "ArchiveRecovery", "Fixtures", "zip-aes.zip");
        vm.HandleDroppedPaths([sample]);
        for (int i = 0; i < 100 && !vm.Status.Contains("ZIP"); i++) await Task.Delay(20);
        Assert.Contains("ZIP", vm.Status);
        Assert.False(vm.IsBusy);
        Assert.Equal(sample, vm.ArchivePath);
    });
    [Fact]
    public Task Recovery_ProtectsInputsPreservesUncertaintyMasksAndClearsResult() => WpfTestThread.RunAsync(async () =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var verifier = new TestVerifier(async token =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return new(++calls == 1 ? ArchivePasswordOutcome.RejectedUncertain : ArchivePasswordOutcome.Match, "safe result");
        });
        using var vm = new ArchiveRecoveryViewModel(() => verifier) { ArchivePath = typeof(App).Assembly.Location, CandidateText = "wrong\n secret 中文 " };
        var run = vm.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(vm.IsBusy);
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.True(vm.StopCommand.CanExecute(null));
        vm.CandidateText = "replaced";
        Assert.NotEqual("replaced", vm.CandidateText);
        release.SetResult();
        await run;
        Assert.True(verifier.Disposed);
        Assert.False(vm.IsBusy);
        Assert.Contains("不确定", vm.Warning);
        Assert.DoesNotContain("secret", vm.PasswordDisplay);
        vm.RevealCommand.Execute(null);
        Assert.Equal(" secret 中文 ", vm.PasswordDisplay);
        vm.CandidateText = "new";
        Assert.False(vm.HasResult);
    });

    [Fact]
    public Task StopAndDispose_CancelPendingVerificationAndAllowRestart() => WpfTestThread.RunAsync(async () =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var verifier = new TestVerifier(async token => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return new(ArchivePasswordOutcome.Match, "unused"); });
        using var vm = new ArchiveRecoveryViewModel(() => verifier) { ArchivePath = typeof(App).Assembly.Location, CandidateText = "secret" };
        var run = vm.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.Stop();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.IsBusy);
        Assert.False(vm.HasResult);
        Assert.Contains("已停止", vm.Status);
        Assert.True(vm.StartCommand.CanExecute(null));
        entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        run = vm.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.Dispose();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.StartCommand.CanExecute(null));
    });

    private sealed class TestVerifier(Func<CancellationToken, Task<ArchivePasswordResult>> verify) : IArchivePasswordVerifier, IDisposable
    {
        public bool Disposed { get; private set; }
        public Task<ArchivePasswordResult> VerifyAsync(string path, string password, TimeSpan timeout, CancellationToken cancellationToken) => verify(cancellationToken);
        public void Dispose() => Disposed = true;
    }
    [Fact]
    public Task Page_RejectsDirectoriesAndDoesNotStartOnDrop() => WpfTestThread.RunAsync(() =>
    {
        Type? type = typeof(MainViewModel).Assembly.GetType("ToolsBox.App.ArchiveRecovery.ArchiveRecoveryViewModel");
        Assert.NotNull(type);
        using var vm = (IDisposable)Activator.CreateInstance(type!)!;
        type!.GetMethod("HandleDroppedPaths")!.Invoke(vm, [new[] { Environment.CurrentDirectory }]);
        Assert.Equal(false, type.GetProperty("IsBusy")!.GetValue(vm));
        Assert.Contains("文件", (string)type.GetProperty("Status")!.GetValue(vm)!);
        Assert.Equal("", type.GetProperty("ArchivePath")!.GetValue(vm));
        return Task.CompletedTask;
    });
}
