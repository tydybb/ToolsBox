using ToolsBox.Core.ArchiveRecovery;

namespace ToolsBox.Core.Tests.ArchiveRecovery;

public class ArchivePasswordRecoveryRunnerTests
{
    [Fact]
    public async Task ContinuesRejectedCandidatesAndOnlyReturnsFullyMatchedPassword()
    {
        var verifier = new FakeVerifier(ArchivePasswordOutcome.NoMatch, ArchivePasswordOutcome.RejectedUncertain, ArchivePasswordOutcome.Match);
        var progress = new List<ArchiveRecoveryProgress>();
        var result = await new ArchivePasswordRecoveryRunner(verifier).RunAsync("archive", new[] { "wrong", "uncertain", "secret", "unused" }, TimeSpan.FromSeconds(1), new InlineProgress(progress));
        Assert.Equal("secret", result.MatchedPassword);
        Assert.Equal(3, result.Attempted);
        Assert.Equal(1, result.UncertainCount);
        Assert.Equal(new long[] { 1, 2, 3 }, progress.Select(p => p.Attempted));
        Assert.Equal(1, verifier.MaxConcurrent);
    }

    [Theory]
    [InlineData(ArchivePasswordOutcome.NotEncrypted)] [InlineData(ArchivePasswordOutcome.Unsupported)]
    [InlineData(ArchivePasswordOutcome.InvalidArchive)] [InlineData(ArchivePasswordOutcome.Inconclusive)]
    public async Task FatalOutcomeStopsWithoutReturningPassword(ArchivePasswordOutcome outcome)
    {
        var result = await new ArchivePasswordRecoveryRunner(new FakeVerifier(outcome)).RunAsync("archive", new[] { "first", "unused" }, TimeSpan.FromSeconds(1));
        Assert.Equal(outcome, result.Verification.Outcome);
        Assert.Null(result.MatchedPassword);
        Assert.Equal(1, result.Attempted);
    }

    [Fact]
    public async Task ExhaustionRetainsUncertaintyWarning()
    {
        var result = await new ArchivePasswordRecoveryRunner(new FakeVerifier(ArchivePasswordOutcome.RejectedUncertain, ArchivePasswordOutcome.NoMatch)).RunAsync("archive", new[] { "a", "b" }, TimeSpan.FromSeconds(1));
        Assert.Equal(ArchivePasswordOutcome.RejectedUncertain, result.Verification.Outcome);
        Assert.Null(result.MatchedPassword);
        Assert.Equal(1, result.UncertainCount);
    }

    [Fact]
    public async Task UserCancellationThrows()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ArchivePasswordRecoveryRunner(new FakeVerifier()).RunAsync("archive", new[] { "a" }, TimeSpan.FromSeconds(1), cancellationToken: cancellation.Token));
    }

    private sealed class InlineProgress(List<ArchiveRecoveryProgress> items) : IProgress<ArchiveRecoveryProgress>
    { public void Report(ArchiveRecoveryProgress value) => items.Add(value); }

    private sealed class FakeVerifier(params ArchivePasswordOutcome[] outcomes) : IArchivePasswordVerifier
    {
        private int active;
        private int index;
        public int MaxConcurrent { get; private set; }
        public async Task<ArchivePasswordResult> VerifyAsync(string path, string password, TimeSpan timeout, CancellationToken cancellationToken)
        {
            MaxConcurrent = Math.Max(MaxConcurrent, ++active);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            active--;
            return new(outcomes[index++], "fixed safe text");
        }
    }
}
