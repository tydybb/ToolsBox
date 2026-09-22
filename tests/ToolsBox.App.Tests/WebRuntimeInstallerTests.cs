using ToolsBox.App.WebResources;
using ToolsBox.MediaDownloads;
using System.IO;

namespace ToolsBox.App.Tests;

public class WebRuntimeInstallerTests
{
    [Theory]
    [InlineData("153.0.1.2", null, null)]
    [InlineData(null, "153.0.1.2", null)]
    [InlineData("153.0.1.2", "154.0.1.2 dev", null)]
    [InlineData("153.0.1.2", "153.0.1.3", "153.0.1.3")]
    public void DetectionRequiresRegisteredAndLoadableStableRuntime(string? registry, string? loader, string? expected)
    {
        Assert.Equal(expected, new WindowsWebRuntimeBackend(() => registry, () => loader).DetectVersion());
    }
    [Fact]
    public async Task InstalledRuntimeSkipsAllInstallationWork()
    {
        var backend = new FakeBackend { Version = "140.0.1.2" };
        Assert.Equal("140.0.1.2", await new WebRuntimeInstaller(backend).InstallAsync());
        Assert.Empty(backend.Calls);
    }
    [Fact]
    public async Task UntrustedInstallerCannotExecute()
    {
        var backend = new FakeBackend { Trusted = false };
        await Assert.ThrowsAsync<InvalidDataException>(() => new WebRuntimeInstaller(backend).InstallAsync());
        Assert.Equal(new[] { "download", "verify" }, backend.Calls);
    }
    [Fact]
    public async Task CancellationAfterLaunchWaitsForInstallerAndDetectsRuntime()
    {
        using var cancel = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new FakeBackend { OnInstall = () => started.SetResult(), InstallGate = gate.Task };
        var service = new WebRuntimeInstaller(backend);
        var installation = service.InstallAsync(ct: cancel.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.True(service.IsInstalling);
            cancel.Cancel();
            Assert.False(installation.IsCompleted);
            Assert.True(service.IsInstalling);
            Assert.Null(backend.Version);
        }
        finally { gate.TrySetResult(); }
        Assert.Equal("140.0.1.2", await installation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(service.IsInstalling);
        Assert.Equal(new[] { "download", "verify", "install" }, backend.Calls);
    }
    [Fact]
    public async Task CancellationBeforeLaunchDoesNotExecuteInstaller()
    {
        using var cancel = new CancellationTokenSource();
        var backend = new FakeBackend { OnVerify = () => cancel.Cancel() };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WebRuntimeInstaller(backend).InstallAsync(ct: cancel.Token));
        Assert.DoesNotContain("install", backend.Calls);
    }
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public async Task FailedInstallerOrMissingPostInstallRuntimeIsRetryable(int exit, bool register)
    {
        var backend = new FakeBackend { ExitCode = exit, Register = register };
        var service = new WebRuntimeInstaller(backend);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync());
        Assert.False(service.IsInstalling);
        backend.ExitCode = 0; backend.Register = true;
        Assert.Equal("140.0.1.2", await service.InstallAsync());
    }
    private sealed class FakeBackend : IWebRuntimeBackend
    {
        public string? Version;
        public bool Trusted = true, Register = true;
        public int ExitCode;
        public Action? OnInstall, OnVerify;
        public Task? InstallGate;
        public List<string> Calls = [];
        public string? DetectVersion() => Version;
        public Task DownloadAsync(string path, IProgress<DownloadProgress>? progress, CancellationToken ct)
        { Calls.Add("download"); return Task.CompletedTask; }
        public Task<bool> VerifyMicrosoftSignatureAsync(string path, CancellationToken ct)
        { Calls.Add("verify"); OnVerify?.Invoke(); return Task.FromResult(Trusted); }
        public async Task<int> InstallAsync(string path)
        {
            Calls.Add("install"); OnInstall?.Invoke();
            if (InstallGate != null) await InstallGate;
            if (Register && ExitCode == 0) Version = "140.0.1.2";
            return ExitCode;
        }
    }
}
