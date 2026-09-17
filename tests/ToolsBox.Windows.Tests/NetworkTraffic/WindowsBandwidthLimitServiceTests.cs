using System.Diagnostics;
using ToolsBox.Core.NetworkTraffic;
using ToolsBox.Windows.NetworkTraffic;

namespace ToolsBox.Windows.Tests.NetworkTraffic;

public sealed class WindowsBandwidthLimitServiceTests
{
    [Fact]
    public async Task SetUploadLimitAsync_CreatesOwnedRuleAndReturnsVerifiedState()
    {
        string path = GetExistingExecutablePath();
        var runner = new FakeQosCommandRunner();
        var service = new WindowsBandwidthLimitService(runner);

        BandwidthLimitRule result = await service.SetUploadLimitAsync(path, 8_000_000);

        Assert.True(result.IsOwned);
        Assert.False(result.HasConflict);
        Assert.Equal(8_000_000UL, result.BitsPerSecond);
        Assert.Equal(QosPolicyName.ForPath(path), runner.CreatedName);
        Assert.Equal(Path.GetFullPath(path), runner.CreatedPath);
    }

    [Fact]
    public async Task SetUploadLimitAsync_RefusesForeignRuleForSamePath()
    {
        string path = GetExistingExecutablePath();
        var runner = new FakeQosCommandRunner();
        runner.Policies.Add(new QosPolicyRecord("CompanyPolicy", path, 4_000_000, "Group Policy"));
        var service = new WindowsBandwidthLimitService(runner);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetUploadLimitAsync(path, 8_000_000));

        Assert.Contains("冲突", error.Message);
        Assert.Null(runner.CreatedName);
    }

    [Fact]
    public async Task SetUploadLimitAsync_UpdatesExistingOwnedRule()
    {
        string path = GetExistingExecutablePath();
        var runner = new FakeQosCommandRunner();
        runner.Policies.Add(new QosPolicyRecord(QosPolicyName.ForPath(path), path, 1_000_000, "PowerShell / WMI"));
        var service = new WindowsBandwidthLimitService(runner);

        BandwidthLimitRule result = await service.SetUploadLimitAsync(path, 2_000_000);

        Assert.Equal(2_000_000UL, result.BitsPerSecond);
        Assert.Equal(QosPolicyName.ForPath(path), runner.UpdatedName);
    }

    [Fact]
    public async Task RemoveUploadLimitAsync_RemovesOnlyOwnedMatchingRule()
    {
        string path = GetExistingExecutablePath();
        var runner = new FakeQosCommandRunner();
        string ownedName = QosPolicyName.ForPath(path);
        runner.Policies.Add(new QosPolicyRecord(ownedName, path, 1_000_000, "PowerShell / WMI"));
        var service = new WindowsBandwidthLimitService(runner);

        await service.RemoveUploadLimitAsync(path);

        Assert.Equal(ownedName, runner.RemovedName);
        Assert.Empty(runner.Policies);
    }

    [Fact]
    public async Task RemoveUploadLimitAsync_DoesNotRemoveForeignRule()
    {
        string path = GetExistingExecutablePath();
        var runner = new FakeQosCommandRunner();
        runner.Policies.Add(new QosPolicyRecord("UserRule", path, 1_000_000, "Group Policy"));
        var service = new WindowsBandwidthLimitService(runner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveUploadLimitAsync(path));

        Assert.Null(runner.RemovedName);
        Assert.Single(runner.Policies);
    }

    private static string GetExistingExecutablePath()
    {
        using Process current = Process.GetCurrentProcess();
        return current.MainModule!.FileName;
    }

    private sealed class FakeQosCommandRunner : IQosCommandRunner
    {
        public List<QosPolicyRecord> Policies { get; } = [];
        public string? CreatedName { get; private set; }
        public string? CreatedPath { get; private set; }
        public string? UpdatedName { get; private set; }
        public string? RemovedName { get; private set; }

        public Task<IReadOnlyList<QosPolicyRecord>> GetPoliciesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<QosPolicyRecord>>(Policies.ToArray());

        public Task CreateAsync(
            string name,
            string executablePath,
            ulong bitsPerSecond,
            CancellationToken cancellationToken = default)
        {
            CreatedName = name;
            CreatedPath = executablePath;
            Policies.Add(new QosPolicyRecord(name, executablePath, bitsPerSecond, "PowerShell / WMI"));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(string name, ulong bitsPerSecond, CancellationToken cancellationToken = default)
        {
            UpdatedName = name;
            int index = Policies.FindIndex(item => item.Name == name);
            Policies[index] = Policies[index] with { BitsPerSecond = bitsPerSecond };
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string name, CancellationToken cancellationToken = default)
        {
            RemovedName = name;
            Policies.RemoveAll(item => item.Name == name);
            return Task.CompletedTask;
        }
    }
}
