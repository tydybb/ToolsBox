namespace ToolsBox.Core.ArchiveRecovery;

public enum ArchivePasswordOutcome { Match, NoMatch, RejectedUncertain, NotEncrypted, Unsupported, InvalidArchive, Inconclusive }

public sealed record ArchivePasswordResult(ArchivePasswordOutcome Outcome, string Message);

public interface IArchivePasswordVerifier
{
    Task<ArchivePasswordResult> VerifyAsync(string path, string password, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed record ArchiveRecoveryProgress(long Attempted, TimeSpan Elapsed, long UncertainCount);

public sealed record ArchiveRecoveryResult(ArchivePasswordResult Verification, string? MatchedPassword, long Attempted, long UncertainCount, TimeSpan Elapsed);
