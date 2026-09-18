using System.Numerics;
using System.Text;
using ToolsBox.Core.ArchiveRecovery;

namespace ToolsBox.Core.Tests.ArchiveRecovery;

public class PasswordCandidatesTests
{
    [Fact]
    public void CancellationInterruptsEmptyLinesAndRules()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => PasswordCandidates.FromText("\n\n", cancel.Token).ToArray());
        Assert.ThrowsAny<OperationCanceledException>(() => PasswordCandidates.FromRules("", "", "ab", 0, 12, cancel.Token).First());
        Assert.ThrowsAny<OperationCanceledException>(() => PasswordCandidates.FromFile("unused", cancel.Token).ToArray());
    }

    [Fact]
    public void FileEnumerationCanCancelWhileSkippingLargeBlankInput()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "first\n" + new string('\n', 4_000_000));
            using var cancel = new CancellationTokenSource();
            using var candidates = PasswordCandidates.FromFile(path, cancel.Token).GetEnumerator();
            Assert.True(candidates.MoveNext());
            cancel.CancelAfter(TimeSpan.FromMilliseconds(1));
            Assert.ThrowsAny<OperationCanceledException>(() => candidates.MoveNext());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FileRejectsOversizedCandidateWithoutIncludingItsContents()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, new string('s', 8193));
            var error = Assert.Throws<InvalidDataException>(() => PasswordCandidates.FromFile(path).ToArray());
            Assert.DoesNotContain(new string('s', 50), error.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TextPreservesSpacesCaseAndUnicodeAndSkipsEmptyLines() =>
        Assert.Equal(new[] { " A ", "a", "密码🙂", " " }, PasswordCandidates.FromText("\uFEFF A \r\n\na\n密码🙂\r \r\n"));

    [Fact]
    public void RulesDeduplicateAndEnumerateByLengthThenCharacterOrder()
    {
        Assert.Equal(new[] { "<> ", "<b> ", "<a> ", "<bb> ", "<ba> ", "<ab> ", "<aa> " }, PasswordCandidates.FromRules("<", "> ", "bba", 0, 2));
        Assert.Equal(new BigInteger(7), PasswordCandidates.CountRules("bba", 0, 2));
        Assert.Equal(new[] { "🙂", "中" }, PasswordCandidates.FromRules("", "", "🙂中🙂", 1, 1));
    }

    [Theory]
    [InlineData(-1, 2)] [InlineData(2, 1)] [InlineData(0, 13)]
    public void RulesRejectInvalidLengths(int min, int max) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordCandidates.FromRules("", "", "ab", min, max).ToArray());

    [Fact]
    public void LargeRulesRemainLazyAndCountDoesNotOverflow()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        Assert.Equal("aaaaaaaaaaaa", PasswordCandidates.FromRules("", "", chars, 12, 12).First());
        Assert.Equal(BigInteger.Pow(62, 12), PasswordCandidates.CountRules(chars, 12, 12));
    }

    [Fact]
    public void FileIsLazyStrictUtf8AndStripsOnlyInitialBom()
    {
        string path = Path.GetTempFileName();
        try
        {
            var candidates = PasswordCandidates.FromFile(path);
            File.WriteAllText(path, " A \r\n密码🙂\n\uFEFFkeep", new UTF8Encoding(true));
            Assert.Equal(new[] { " A ", "密码🙂", "\uFEFFkeep" }, candidates);
            File.WriteAllBytes(path, new byte[] { 0xC3, 0x28 });
            Assert.Throws<DecoderFallbackException>(() => candidates.ToArray());
        }
        finally { File.Delete(path); }
    }
}
