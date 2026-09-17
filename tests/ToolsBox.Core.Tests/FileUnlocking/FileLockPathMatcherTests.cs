using ToolsBox.Core.FileUnlocking;

namespace ToolsBox.Core.Tests.FileUnlocking;

public sealed class FileLockPathMatcherTests
{
    [Fact]
    public void Matches_FileTarget_RequiresExactPathIgnoringCase()
    {
        FileLockTarget target = FileLockTarget.Create(@"C:\Work\Data.txt", false);

        Assert.True(FileLockPathMatcher.Matches(target, @"\\?\C:\work\DATA.txt"));
        Assert.False(FileLockPathMatcher.Matches(target, @"C:\Work\Data.txt.bak"));
    }

    [Fact]
    public void Matches_DirectoryTarget_IncludesDirectoryAndDescendantsOnly()
    {
        FileLockTarget target = FileLockTarget.Create(@"C:\Work\Locked", true);

        Assert.True(FileLockPathMatcher.Matches(target, @"C:\Work\Locked"));
        Assert.True(FileLockPathMatcher.Matches(target, @"C:\Work\Locked\child\data.bin"));
        Assert.False(FileLockPathMatcher.Matches(target, @"C:\Work\Locked-Other\data.bin"));
    }

    [Theory]
    [InlineData(@"\\?\C:\Temp\item.txt", @"C:\Temp\item.txt")]
    [InlineData(@"C:\Temp\Folder\\", @"C:\Temp\Folder")]
    [InlineData(@"\\?\UNC\server\share\item.txt", @"\\server\share\item.txt")]
    public void Normalize_RemovesExtendedPrefixAndTrailingSeparators(string input, string expected)
    {
        Assert.Equal(expected, FileLockPathMatcher.Normalize(input));
    }
}
