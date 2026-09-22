using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ToolsBox.App.WebResources;
using ToolsBox.Core.WebResources;

namespace ToolsBox.App.Tests.Startup;

public class PlayerResourcePublicationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string MediaUrl = "https://media.example.test/video.mp4";
    private static PlayerDocument Document(string source = MediaUrl, int generation = 1) => new()
    {
        Document = "document-1", Url = "https://www.douyin.com/video/123", Title = "当前视频",
        Players = [new() { Id = "p1", Source = source, Generation = generation, Visible = true, Duration = 12, SiteId = "123" }]
    };

    [Fact]
    public Task BilibiliEpisodeUsesPageExtractionNotCapturedVideoRoute() => Run(window =>
    {
        var document = Document() with { Url = "https://www.bilibili.com/bangumi/play/ss33415", Title = "番剧",
            Players = [new() { Id="p1", Source="blob:https://www.bilibili.com/media", SiteId="323085", Visible=true, Duration=1494 }] };
        Publish(window, document);
        var row = Assert.Single(Rows(window));
        Assert.Equal("https://www.bilibili.com/bangumi/play/ep323085", row.Url);
        Assert.False(row.CapturedVideo);
        Assert.Equal(1494, row.ExpectedDuration);
        Assert.Contains("合并音视频", row.SelectedFormat.Label);
    });

    [Fact]
    public Task PollPublicationAddsNamedDownloadableVideoWithoutStartingTask() => Run(window =>
    {
        Publish(window, Document());
        var row = Assert.Single(Rows(window));
        Assert.Equal("当前视频", row.Name);
        Assert.True(row.CapturedVideo);
        Assert.Equal(12, row.ExpectedDuration);
        Assert.Empty((ObservableCollection<WebDownloadRow>)Field(window, "_tasks"));
    });

    [Fact]
    public Task RepeatedPublicationPreservesSelectionOrderAndRowIdentity() => Run(window =>
    {
        Publish(window, Document());
        var first = Rows(window)[0]; first.IsSelected = true;
        Rows(window).Add(new("https://image.test/a.png", Document().Url, WebResourceKind.Image, 10));
        Publish(window, Document());
        Assert.Equal(2, Rows(window).Count);
        Assert.Same(first, Rows(window)[0]);
        Assert.True(first.IsSelected);
    });

    [Fact]
    public Task RawNetworkRowIsPromotedInPlaceWithoutMutatingQueuedResource() => Run(window =>
    {
        var raw = new WebResourceRow(MediaUrl, Document().Url, WebResourceKind.Video, 1234) { IsSelected = true };
        Rows(window).Add(raw);
        var task = new WebDownloadRow(raw, Path.GetTempPath());
        Publish(window, Document());
        var promoted = Assert.Single(Rows(window));
        Assert.True(promoted.CapturedVideo);
        Assert.True(promoted.IsSelected);
        Assert.Equal(1234, promoted.Size);
        Assert.False(task.Resource.CapturedVideo);
        Assert.NotSame(raw, promoted);
    });

    [Fact]
    public Task ClearSuppressesSamePlayerButNewSourceCanAppear() => Run(window =>
    {
        Publish(window, Document());
        Invoke(window, "ClearResources", window, new RoutedEventArgs());
        Publish(window, Document());
        Assert.Empty(Rows(window));
        var changed = "https://media.example.test/next.mp4";
        ((Dictionary<string, WebResourceKind>)Field(window, "_playerObservedMedia"))[changed] = WebResourceKind.Video;
        Publish(window, Document(changed, 2));
        Assert.Equal(changed, Assert.Single(Rows(window)).Url);
    });

    [Fact]
    public Task NewDocumentCanRebindCachedMediaWithoutRetainingOldRows() => Run(window =>
    {
        Publish(window, Document());
        var old = Assert.Single(Rows(window));
        Invoke(window, "ResetPlayersForNavigation");
        Assert.Empty(Rows(window));

        Publish(window, Document() with { Document = "new-document", Url = "https://www.douyin.com/video/456" });

        var current = Assert.Single(Rows(window));
        Assert.NotSame(old, current);
        Assert.True(current.CapturedVideo);
        Assert.Equal("https://www.douyin.com/video/456", current.PageUrl);
    });

    [Fact]
    public Task NavigationResetRemovesAllResourcesAndSelectionWithoutClearingDownloadTasks() => Run(window =>
    {
        Publish(window, Document());
        var video = Assert.Single(Rows(window));
        video.IsSelected = true;
        Rows(window).Add(new("https://image.test/old.png", Document().Url, WebResourceKind.Image, 10) { IsSelected = true });
        ((DataGrid)window.FindName("ResourcesGrid")).SelectedItem = video;
        var tasks = (ObservableCollection<WebDownloadRow>)Field(window, "_tasks");
        var task = new WebDownloadRow(video, Path.GetTempPath()) { IsActive = false };
        tasks.Add(task);

        Invoke(window, "ResetPlayersForNavigation");

        Assert.Empty(Rows(window));
        Assert.Null(((DataGrid)window.FindName("ResourcesGrid")).SelectedItem);
        Assert.False(((CheckBox)window.FindName("SelectAllResources")).IsEnabled);
        Assert.Same(task, Assert.Single(tasks));
        Assert.Same(video, task.Resource);
    });

    [Fact]
    public Task ClearedMediaUrlSuppressionResetsOnNavigation() => Run(window =>
    {
        Publish(window, Document());
        Assert.False((bool)Invoke(window, "IsClearedPlayerMediaUrl", MediaUrl)!);
        Invoke(window, "ClearResources", window, new RoutedEventArgs());
        Assert.True((bool)Invoke(window, "IsClearedPlayerMediaUrl", MediaUrl)!);
        Assert.False((bool)Invoke(window, "IsClearedPlayerMediaUrl", "https://media.example.test/next.mp4")!);
        Invoke(window, "ResetPlayersForNavigation");
        Assert.False((bool)Invoke(window, "IsClearedPlayerMediaUrl", MediaUrl)!);
    });

    [Fact]
    public Task NewPlayerIdentityCanPublishWhileItsClearedUrlStaysSuppressedForRawRows() => Run(window =>
    {
        Publish(window, Document());
        Invoke(window, "ClearResources", window, new RoutedEventArgs());
        Publish(window, Document(generation: 2));
        Assert.True(Assert.Single(Rows(window)).CapturedVideo);
        Assert.True((bool)Invoke(window, "IsClearedPlayerMediaUrl", MediaUrl)!);
    });

    [Fact]
    public Task ExplicitConfirmationReleasesAllClearedBindingsForTheSameMediaUrl() => Run(window =>
    {
        Publish(window, Document());
        Invoke(window, "ClearResources", window, new RoutedEventArgs());
        Publish(window, Document(generation: 2));
        var row = Assert.Single(Rows(window));
        var binding = typeof(WebResourceRow).GetProperty("PlayerBinding", Private)!.GetValue(row)!;
        var candidate = binding.GetType().GetProperty("Candidate")!.GetValue(binding)!;
        var choice = binding.GetType().GetProperty("Choice")!.GetValue(binding)!;
        Invoke(window, "ClearResources", window, new RoutedEventArgs());
        Invoke(window, "PublishPlayerResource", candidate, choice, true);
        Assert.True(Assert.Single(Rows(window)).CapturedVideo);
        Assert.False((bool)Invoke(window, "IsClearedPlayerMediaUrl", MediaUrl)!);
    });

    [Fact]
    public Task DisabledCaptureAndHiddenOrEncryptedPlayersAreNotPublished() => Run(window =>
    {
        var document = Document();
        Publish(window, document with { Players = [document.Players[0] with { Visible = false }] });
        Publish(window, document with { Players = [document.Players[0] with { Encrypted = true }] });
        ((CheckBox)window.FindName("CaptureEnabled")).IsChecked = false;
        Publish(window, document);
        Assert.Empty(Rows(window));
    });

    [Fact]
    public Task ReusedUrlForNewPlayerGenerationDoesNotInheritSelection() => Run(window =>
    {
        Publish(window, Document());
        var old = Rows(window)[0]; old.IsSelected = true;
        Publish(window, Document(generation: 2));
        var current = Assert.Single(Rows(window));
        Assert.NotSame(old, current);
        Assert.False(current.IsSelected);
    });

    [Fact]
    public Task DouyinBlobMetadataIsPublishedBeforeOverlayClick() => Run(window =>
    {
        var items = (Dictionary<string, DouyinMediaItem>)Field(window, "_douyinMedia");
        foreach (var item in DouyinMediaParser.Parse("""{"aweme_detail":{"aweme_id":"123","desc":"识别的视频","video":{"duration":12000,"play_addr":{"url_list":["https://media.example.test/video.mp4"]}}}}""")) items[item.Id] = item;
        Publish(window, Document("blob:https://www.douyin.com/opaque"));
        Assert.Equal("识别的视频", Assert.Single(Rows(window)).Name);
        Assert.True(Rows(window)[0].CapturedVideo);
    });

    private static Task Run(Action<WebResourceWindow> test) => WpfTestThread.RunAsync(() =>
    {
        var window = new WebResourceWindow(Path.Combine(Path.GetTempPath(), "ToolsBox-player-publication-" + Guid.NewGuid().ToString("N")));
        try
        {
            ((Dictionary<string, WebResourceKind>)Field(window, "_playerObservedMedia"))[MediaUrl] = WebResourceKind.Video;
            test(window);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    private static object Field(WebResourceWindow window, string name) => typeof(WebResourceWindow).GetField(name, Private)!.GetValue(window)!;
    private static ObservableCollection<WebResourceRow> Rows(WebResourceWindow window) => (ObservableCollection<WebResourceRow>)Field(window, "_resources");
    private static object? Invoke(WebResourceWindow window, string name, params object[] args)
    {
        var method = typeof(WebResourceWindow).GetMethod(name, Private);
        Assert.NotNull(method);
        return method.Invoke(window, args);
    }
    private static void Publish(WebResourceWindow window, PlayerDocument document) => Invoke(window, "PublishPlayerResources", 0, document);
}
