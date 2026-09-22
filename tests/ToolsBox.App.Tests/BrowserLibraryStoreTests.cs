using System.IO;
using ToolsBox.App.WebResources;

namespace ToolsBox.App.Tests;

public sealed class BrowserLibraryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ToolsBox-BrowserLibrary-" + Guid.NewGuid().ToString("N"));
    private string StorePath => Path.Combine(_directory, "library.json");

    [Fact]
    public void FavoritesPersistRenameDeduplicateAndRemoveWithoutChangingUrl()
    {
        var store = new BrowserLibraryStore(StorePath);
        const string url = "https://example.com/path?query=value#anchor";
        Assert.Empty(store.Favorites);
        store.AddFavorite(url, " First ");
        store.AddFavorite(url, " Second ");
        Assert.Single(store.Favorites);
        store.RenameFavorite(url, " 收藏夹 ");
        var reopened = new BrowserLibraryStore(StorePath);
        Assert.Equal(url, Assert.Single(reopened.Favorites).Url);
        Assert.Equal("收藏夹", reopened.Favorites[0].Title);
        reopened.RemoveFavorite(url);
        Assert.Empty(new BrowserLibraryStore(StorePath).Favorites);
    }

    [Fact]
    public void HistoryKeepsLatestFiveHundredDistinctUrlsAndPersistsRemovalAndClear()
    {
        var store = new BrowserLibraryStore(StorePath);
        for (var i = 0; i < 502; i++) store.RecordVisit($"https://example.com/{i}", $"Page {i}");
        store.RecordVisit("https://example.com/4", "Again");
        var reopened = new BrowserLibraryStore(StorePath);
        Assert.Equal(500, reopened.History.Count);
        Assert.Equal("https://example.com/4", reopened.History[0].Url);
        Assert.Equal("Again", reopened.History[0].Title);
        Assert.DoesNotContain(reopened.History, item => item.Url == "https://example.com/0");
        reopened.RemoveHistory("https://example.com/4");
        Assert.Equal(499, new BrowserLibraryStore(StorePath).History.Count);
        reopened.ClearHistory();
        Assert.Empty(new BrowserLibraryStore(StorePath).History);
    }

    [Theory]
    [InlineData("file:///C:/secret")]
    [InlineData("https://user:password@example.com/")]
    [InlineData("javascript:alert(1)")]
    public void InvalidUrlsAreRejectedWithoutWriting(string url)
    {
        var store = new BrowserLibraryStore(StorePath);
        Assert.Throws<ArgumentException>(() => store.AddFavorite(url, "Title"));
        Assert.Throws<ArgumentException>(() => store.RecordVisit(url, "Title"));
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public void TitlesAreTrimmedAndBoundedAndOversizeUrlsRejected()
    {
        var store = new BrowserLibraryStore(StorePath);
        store.AddFavorite("https://example.com/", "  " + new string('字', 350) + "  ");
        Assert.Equal(new string('字', 300), store.Favorites[0].Title);
        Assert.Throws<ArgumentException>(() => store.AddFavorite("https://example.com/" + new string('x', 8192), "Title"));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Favorites\":null,\"History\":[]}")]
    [InlineData("{\"Favorites\":[{\"Url\":\"file:///secret\",\"Title\":\"x\"}],\"History\":[]}")]
    public void CorruptDataIsReportedAndPreserved(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StorePath, content);
        Assert.Throws<InvalidDataException>(() => new BrowserLibraryStore(StorePath));
        Assert.Equal(content, File.ReadAllText(StorePath));
    }

    [Fact]
    public void OversizedSavedFileIsRejected()
    {
        Directory.CreateDirectory(_directory);
        using (var file = File.Create(StorePath)) file.SetLength(8 * 1024 * 1024 + 1);
        Assert.Throws<InvalidDataException>(() => new BrowserLibraryStore(StorePath));
    }

    [Fact]
    public void MultipleStoresMergeChangesAndDoNotResurrectClearedHistory()
    {
        var first = new BrowserLibraryStore(StorePath);
        var second = new BrowserLibraryStore(StorePath);
        first.AddFavorite("https://example.com/favorite", "Favorite");
        second.RecordVisit("https://example.com/visit", "Visit");
        Assert.Single(new BrowserLibraryStore(StorePath).Favorites);
        first.ClearHistory();
        second.AddFavorite("https://example.com/another", "Another");
        var reopened = new BrowserLibraryStore(StorePath);
        Assert.Equal(2, reopened.Favorites.Count);
        Assert.Empty(reopened.History);
    }

    [Fact]
    public void MutationDoesNotOverwriteNewlyCorruptedFile()
    {
        var store = new BrowserLibraryStore(StorePath);
        store.AddFavorite("https://example.com/", "Original");
        File.WriteAllText(StorePath, "{broken");
        Assert.Throws<InvalidDataException>(() => store.RecordVisit("https://example.com/visit", "Visit"));
        Assert.Equal("{broken", File.ReadAllText(StorePath));
        Assert.Empty(store.History);
        Assert.Single(store.Favorites);
    }

    [Fact]
    public void FailedWritePreservesStateAndLastSavedDataAndRemovesTemporaryFile()
    {
        var store = new BrowserLibraryStore(StorePath);
        store.AddFavorite("https://example.com/", "Original");
        using (var locked = new FileStream(StorePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = Record.Exception(() => store.RenameFavorite("https://example.com/", "Changed"));
            Assert.True(failure is IOException or UnauthorizedAccessException, $"Expected a file access failure, got {failure}");
        }
        Assert.Equal("Original", store.Favorites[0].Title);
        Assert.Equal("Original", new BrowserLibraryStore(StorePath).Favorites[0].Title);
        Assert.Single(Directory.GetFiles(_directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
