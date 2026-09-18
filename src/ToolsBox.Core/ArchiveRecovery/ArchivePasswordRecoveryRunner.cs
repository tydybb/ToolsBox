using System.Diagnostics;

namespace ToolsBox.Core.ArchiveRecovery;

public sealed class ArchivePasswordRecoveryRunner(IArchivePasswordVerifier verifier)
{
    public async Task<ArchiveRecoveryResult> RunAsync(string path, IEnumerable<string> candidates, TimeSpan attemptTimeout, IProgress<ArchiveRecoveryProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (attemptTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(attemptTimeout));
        cancellationToken.ThrowIfCancellationRequested();
        var clock = Stopwatch.StartNew();
        long attempted = 0, uncertain = 0;
        foreach (string candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArchivePasswordResult result = await verifier.VerifyAsync(path, candidate, attemptTimeout, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            attempted++;
            if (result.Outcome == ArchivePasswordOutcome.RejectedUncertain) uncertain++;
            progress?.Report(new(attempted, clock.Elapsed, uncertain));
            if (result.Outcome is ArchivePasswordOutcome.NoMatch or ArchivePasswordOutcome.RejectedUncertain) continue;
            return new(result, result.Outcome == ArchivePasswordOutcome.Match ? candidate : null, attempted, uncertain, clock.Elapsed);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(new(uncertain > 0 ? ArchivePasswordOutcome.RejectedUncertain : ArchivePasswordOutcome.NoMatch,
            uncertain > 0 ? "候选已尝试完；部分结果为密码可能不符或压缩包损坏，无法排除损坏。" : "候选已尝试完，未找到匹配密码。"), null, attempted, uncertain, clock.Elapsed);
    }
}
