using ToolsBox.Core.NetworkTraffic;

namespace ToolsBox.Windows.NetworkTraffic;

public sealed class WindowsBandwidthLimitService : IBandwidthLimitService
{
    private readonly IQosCommandRunner _runner;

    public WindowsBandwidthLimitService(IQosCommandRunner? runner = null)
    {
        _runner = runner ?? new PowerShellQosCommandRunner();
    }

    public BandwidthDirection SupportedDirections => BandwidthDirection.Upload;

    public async Task<IReadOnlyList<BandwidthLimitRule>> GetRulesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<QosPolicyRecord> policies = await _runner.GetPoliciesAsync(cancellationToken).ConfigureAwait(false);
        return policies
            .Where(item => !string.IsNullOrWhiteSpace(item.ExecutablePath) && item.BitsPerSecond > 0)
            .Select(item => ToRule(item, policies))
            .ToArray();
    }

    public async Task<BandwidthLimitRule> SetUploadLimitAsync(
        string executablePath,
        ulong bitsPerSecond,
        CancellationToken cancellationToken = default)
    {
        if (bitsPerSecond == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bitsPerSecond), "限速值必须大于零。");
        }

        string fullPath = ValidateExistingExecutable(executablePath);
        string ruleName = QosPolicyName.ForPath(fullPath);
        IReadOnlyList<QosPolicyRecord> policies = await _runner.GetPoliciesAsync(cancellationToken).ConfigureAwait(false);
        QosPolicyRecord[] matching = policies.Where(item => PathsEqual(item.ExecutablePath, fullPath)).ToArray();
        QosPolicyRecord? foreign = matching.FirstOrDefault(item => !QosPolicyName.IsOwned(item.Name));
        if (foreign is not null)
        {
            throw new InvalidOperationException($"程序存在外部 QoS 规则“{foreign.Name}”冲突，未修改系统策略。");
        }

        QosPolicyRecord? owned = matching.FirstOrDefault(item =>
            string.Equals(item.Name, ruleName, StringComparison.Ordinal));
        if (owned is null)
        {
            await _runner.CreateAsync(ruleName, fullPath, bitsPerSecond, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _runner.UpdateAsync(ruleName, bitsPerSecond, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<QosPolicyRecord> verified = await _runner.GetPoliciesAsync(cancellationToken).ConfigureAwait(false);
        QosPolicyRecord? result = verified.FirstOrDefault(item =>
            string.Equals(item.Name, ruleName, StringComparison.Ordinal) && PathsEqual(item.ExecutablePath, fullPath));
        if (result is null || result.BitsPerSecond != bitsPerSecond)
        {
            throw new InvalidOperationException("上传限速规则未能通过系统状态校验。");
        }

        return ToRule(result, verified);
    }

    public async Task RemoveUploadLimitAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        string fullPath = NormalizeFullPath(executablePath);
        string ruleName = QosPolicyName.ForPath(fullPath);
        IReadOnlyList<QosPolicyRecord> policies = await _runner.GetPoliciesAsync(cancellationToken).ConfigureAwait(false);
        QosPolicyRecord[] matching = policies.Where(item => PathsEqual(item.ExecutablePath, fullPath)).ToArray();
        if (matching.Any(item => !QosPolicyName.IsOwned(item.Name)))
        {
            throw new InvalidOperationException("程序存在外部 QoS 规则冲突，不能显示为不限速。");
        }

        QosPolicyRecord? owned = matching.FirstOrDefault(item =>
            string.Equals(item.Name, ruleName, StringComparison.Ordinal));
        if (owned is null)
        {
            return;
        }

        await _runner.RemoveAsync(ruleName, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<QosPolicyRecord> verified = await _runner.GetPoliciesAsync(cancellationToken).ConfigureAwait(false);
        if (verified.Any(item => string.Equals(item.Name, ruleName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("上传限速规则删除后仍然存在。");
        }
    }

    private static BandwidthLimitRule ToRule(
        QosPolicyRecord policy,
        IReadOnlyList<QosPolicyRecord> allPolicies)
    {
        bool owned = QosPolicyName.IsOwned(policy.Name);
        QosPolicyRecord? conflict = allPolicies.FirstOrDefault(item =>
            !ReferenceEquals(item, policy) &&
            PathsEqual(item.ExecutablePath, policy.ExecutablePath) &&
            !QosPolicyName.IsOwned(item.Name));
        bool hasConflict = !owned || conflict is not null;
        string? reason = !owned
            ? $"外部 QoS 规则：{policy.Name}"
            : conflict is not null
                ? $"与外部 QoS 规则“{conflict.Name}”冲突"
                : null;

        return new BandwidthLimitRule(
            policy.Name,
            Path.GetFullPath(policy.ExecutablePath),
            BandwidthDirection.Upload,
            policy.BitsPerSecond,
            owned,
            hasConflict,
            reason);
    }

    private static string ValidateExistingExecutable(string executablePath)
    {
        string fullPath = NormalizeFullPath(executablePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("程序路径不存在，无法创建限速规则。", fullPath);
        }

        return fullPath;
    }

    private static string NormalizeFullPath(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (executablePath.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("程序路径无效。", nameof(executablePath));
        }

        return Path.GetFullPath(executablePath);
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(QosPolicyName.NormalizePath(left), QosPolicyName.NormalizePath(right), StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
