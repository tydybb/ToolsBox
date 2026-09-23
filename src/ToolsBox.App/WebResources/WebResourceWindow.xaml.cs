using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using ToolsBox.Core.WebResources;
using ToolsBox.MediaDownloads;

namespace ToolsBox.App.WebResources;

public partial class WebResourceWindow : Window
{
    private readonly ObservableCollection<WebResourceRow> _resources = [];
    private readonly HashSet<WebResourceRow> _observedResources = [];
    private readonly PageResourceRequestTracker _resourceRequests = new();
    private readonly PageResourceRequestTracker _documentResourceRequests = new();
    private bool _updatingSelection;
    private readonly ObservableCollection<WebDownloadRow> _tasks = [];
    private readonly string _dataDirectory;
    private readonly ComponentManager _components;
    private readonly MediaDownloadService _downloads;
    private readonly SemaphoreSlim _slots = new(2);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Task> _operations = [];
    private bool _forceClose;
    internal void CloseForToolboxExit(){_forceClose=true;Close();}
    private bool _cleanupComplete;
    private bool _closing, _closed, _browserInitializing;
    private EnvironmentWindow? _environmentWindow;
    private string _page = "";
    private System.Windows.Threading.DispatcherTimer? _parentTimer;
    public void WatchParent(int processId, long startTicks)
    {
        _parentTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _parentTimer.Tick += (_, _) =>
        {
            try
            {
                using var parent = Process.GetProcessById(processId);
                if (!parent.HasExited && parent.StartTime.ToUniversalTime().Ticks == startTicks) return;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { return; }
            _forceClose = true;
            if (_environmentWindow != null) { _environmentWindow.RequestCloseForParentExit(); return; }
            _parentTimer.Stop(); Close();
        };
        _parentTimer.Start();
    }
    public WebResourceWindow() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolsBox")) { }
    public WebResourceWindow(string dataDirectory)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory);
        InitializeComponent();
        InitializeLibrary();
        _components = new ComponentManager(Path.Combine(_dataDirectory, "MediaComponents"));
        _downloads = new MediaDownloadService(_components);
        ResourcesGrid.ItemsSource = _resources; TasksGrid.ItemsSource = _tasks;
        CollectionViewSource.GetDefaultView(_resources).Filter = Filter;
        _resources.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                foreach (var row in _observedResources) row.PropertyChanged -= ResourceSelectionChanged;
                _observedResources.Clear();
            }
            else if (args.OldItems != null) foreach (WebResourceRow row in args.OldItems)
            { row.PropertyChanged -= ResourceSelectionChanged; _observedResources.Remove(row); }
            if (args.NewItems != null) foreach (WebResourceRow row in args.NewItems)
                if (_observedResources.Add(row)) row.PropertyChanged += ResourceSelectionChanged;
            UpdateSelectionSummary();
        };
        Loaded += InitializeBrowser;
        Closing += OnClosing;
        Closed += (_, _) => { _closed = true; _parentTimer?.Stop(); _playerTimer?.Stop(); _lifetime.Cancel(); _playerFrames.Clear(); _frameGenerations.Clear(); _frameSources.Clear(); _navigatingFrames.Clear(); _playerDocuments.Clear(); _douyinMedia.Clear(); _playerObservedMedia.Clear(); _browserPopup?.Close(); Browser.Dispose(); };
    }

    private async void InitializeBrowser(object sender, RoutedEventArgs e)
    {
        Loaded -= InitializeBrowser;
        await InitializeBrowserAsync();
    }
    private async Task InitializeBrowserAsync()
    {
        if (_browserInitializing || Browser.CoreWebView2 != null || _closing) return;
        _browserInitializing = true;
        if (!WebResourceLauncher.CanRunBrowser(WebResourceLauncher.IsAdministrator()))
        { MessageBox.Show(this, "安全检查失败：浏览窗口不能以管理员身份运行。"); Close(); return; }
        try
        {
            // Stable user-data directory preserves the existing normal profile across EXE upgrades/restarts.
            var directory = Path.Combine(_dataDirectory, "WebProfile");
            var environment = await CoreWebView2Environment.CreateAsync(null, directory);
            if (_closed) return;
            await Browser.EnsureCoreWebView2Async(environment);
            if (_closed || _closing) return;
            var core = Browser.CoreWebView2 ?? throw new InvalidOperationException("浏览器尚未初始化。");
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.NavigationStarting += (_, args) =>
            {
                if (!WebResourceRules.IsWebUrl(args.Uri)) { args.Cancel = true; return; }
                ResetPlayersForNavigation();
                _page = args.Uri; Address.Text = args.Uri;
            };
            core.SourceChanged += (_, args) =>
            {
                if (_closed || _closing || !WebResourceRules.IsWebUrl(core.Source)) return;
                if (_page != core.Source)
                {
                    // pushState/popstate keep the DOM and loaded player media, but not the old page's list.
                    if (args.IsNewDocument) ResetPlayersForNavigation();
                    else ResetResourcesForPageChange();
                }
                _page = core.Source; Address.Text = _page;
            };
            core.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess) RecordCompletedPage(core.Source, core.DocumentTitle);
            };
            core.NewWindowRequested += BrowserNewWindowRequested;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => { args.Cancel = true; Status.Text = "已阻止网页直接下载，请使用资源列表选择并下载。"; };
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (_, args) =>
            {
                if (_closing || _closed) return;
                _resourceRequests.ObserveRequest(args.Request.Method, args.Request.Uri, _playerDocumentGeneration);
                _documentResourceRequests.ObserveRequest(args.Request.Method, args.Request.Uri, _playerEvidenceDocumentGeneration);
                if (_resourceRequests.IsSaturated || _documentResourceRequests.IsSaturated)
                    Status.Text = "未完成请求记录已达安全上限，请重新打开网页资源窗口后继续识别。";
            };
            core.WebResourceResponseReceived += ResourceReceived;
            await InitializePlayers(core);
            core.ProcessFailed += (_, _) => Status.Text = "浏览器进程已退出，请关闭后重新打开网页资源工具。";
            await RefreshComponentStatus();
            Status.Text = _libraryLoadFailed ? "浏览器已就绪，但收藏/历史文件无法读取，已保留原文件并禁用修改。" : "浏览器已就绪 · 登录数据在本机保留。输入网址、播放视频或滚动页面即可识别资源。";
        }
        catch { Status.Text = "浏览器初始化失败。请点击“环境检测”检查 Microsoft WebView2 Runtime。"; }
        finally { _browserInitializing = false; }
    }

    private async void ResourceReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (_closed) return;
        try
        {
            string url = e.Request.Uri;
            // Consume all responses, even excluded HTTP errors or while capture is paused.
            // SPA address changes invalidate rows, but in-flight requests still belong to
            // the same physical document and may supply evidence for its next player.
            bool currentPage = _resourceRequests.ConsumeResponse(e.Request.Method, url, _playerDocumentGeneration);
            bool currentDocument = _documentResourceRequests.ConsumeResponse(e.Request.Method, url, _playerEvidenceDocumentGeneration);
            if (!currentDocument ||
                _closing || CaptureEnabled.IsChecked != true || e.Response.StatusCode is < 200 or >= 300) return;
            long pageGeneration = _playerDocumentGeneration, documentGeneration = _playerEvidenceDocumentGeneration;
            await ObservePlayerMetadata(e);
            if (_closing || _closed || CaptureEnabled.IsChecked != true || documentGeneration != _playerEvidenceDocumentGeneration) return;
            var kind = WebResourceRules.Classify(url, e.Response.Headers.Contains("Content-Type") ? e.Response.Headers.GetHeader("Content-Type") : "");
            if (kind is WebResourceKind.Video or WebResourceKind.Hls or WebResourceKind.Dash)
            {
                if (!_playerObservedMedia.ContainsKey(url) && _playerObservedMedia.Count >= 2000)
                    _playerObservedMedia.Remove(_playerObservedMedia.Keys.First());
                _playerObservedMedia[url] = kind.Value;
            }
            // Bounded evidence may be rebound only by a fresh player snapshot. It does
            // not give an old page's raw response permission to populate the new list.
            if (!currentPage || pageGeneration != _playerDocumentGeneration) return;
            if (kind is null || IsClearedPlayerMediaUrl(url) || _resources.Any(r => r.Url == url && r.PageUrl == _page)) return;
            if (_resources.Count >= 2000) { Status.Text = "已达到 2000 条资源上限，请清空列表后继续识别。"; return; }
            long? size = e.Response.Headers.Contains("Content-Length") && long.TryParse(e.Response.Headers.GetHeader("Content-Length"), out var length) ? length : null;
            var row = new WebResourceRow(url, _page, kind.Value, size); _resources.Add(row);
            if (kind == WebResourceKind.Image && size is > 0 and <= 2 * 1024 * 1024 && _resources.Count < 300)
            {
                using var stream = await e.Response.GetContentAsync();
                if (_closed || stream is null) return;
                using var memory = new MemoryStream();
                var bytes = new byte[8192]; int count;
                while ((count = await stream.ReadAsync(bytes, _lifetime.Token)) > 0)
                { if (memory.Length + count > 2 * 1024 * 1024) return; await memory.WriteAsync(bytes.AsMemory(0, count), _lifetime.Token); }
                memory.Position = 0;
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 96; image.StreamSource = memory; image.EndInit(); image.Freeze(); row.Thumbnail = image;
            }
        }
        catch { /* Resource failures must not interrupt the browser or expose signed URLs. */ }
    }

    private bool Filter(object item) => item is WebResourceRow row && (KindFilter.SelectedIndex == 0 || (KindFilter.SelectedIndex == 1 ? !row.IsVideo : row.IsVideo));
    private void FilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResourcesGrid?.ItemsSource == null) return;
        CollectionViewSource.GetDefaultView(_resources).Refresh(); UpdateSelectionSummary();
    }
    private WebResourceRow[] VisibleResources() => CollectionViewSource.GetDefaultView(_resources).Cast<WebResourceRow>().ToArray();
    private void ResourceSelectionChanged(object? sender, PropertyChangedEventArgs e)
    { if (!_updatingSelection && e.PropertyName == nameof(WebResourceRow.IsSelected)) UpdateSelectionSummary(); }
    private void UpdateSelectionSummary()
    {
        var visible = VisibleResources();
        int count = visible.Count(row => row.IsSelected);
        SelectAllResources.IsEnabled = visible.Length > 0;
        SelectAllResources.IsChecked = count == 0 ? false : count == visible.Length ? true : null;
        int hidden = _resources.Count(row => row.IsSelected) - count;
        SelectionSummary.Text = $"当前勾选 {count}/{visible.Length}" + (hidden > 0 ? $" · 隐藏已选 {hidden}（本次不下载）" : "");
    }
    private void SetVisibleSelection(bool selected)
    {
        _updatingSelection = true;
        try { foreach (var row in VisibleResources()) row.IsSelected = selected; }
        finally { _updatingSelection = false; }
        UpdateSelectionSummary();
    }
    private void ToggleSelectAll(object sender, RoutedEventArgs e) => SetVisibleSelection(!VisibleResources().All(row => row.IsSelected));
    private void Navigate(object sender, RoutedEventArgs e)
    {
        string url = Address.Text.Trim(); if (!url.Contains("://")) url = "https://" + url;
        if (!WebResourceRules.IsWebUrl(url)) { Status.Text = "请输入不含账号密码的 HTTP(S) 地址。"; return; }
        if (Browser.CoreWebView2 is null) { Status.Text = "浏览器尚未就绪。"; return; }
        Browser.CoreWebView2.Navigate(url);
    }
    private void AddressKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Navigate(sender, e); }
    private void GoBack(object sender, RoutedEventArgs e) { if (Browser.CoreWebView2?.CanGoBack == true) Browser.GoBack(); }
    private void GoForward(object sender, RoutedEventArgs e) { if (Browser.CoreWebView2?.CanGoForward == true) Browser.GoForward(); }
    private void Reload(object sender, RoutedEventArgs e) { if (Browser.CoreWebView2 != null) Browser.Reload(); }
    private void SelectAll(object sender, RoutedEventArgs e) => SetVisibleSelection(true);
    private void ClearSelection(object sender, RoutedEventArgs e) => SetVisibleSelection(false);
    private void ClearResources(object sender, RoutedEventArgs e) => ClearPlayerResources();
    private void ChooseFolder(object sender, RoutedEventArgs e)
    { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) OutputFolder.Text = dialog.FolderName; }
    private async void ClearSiteData(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2 == null || _tasks.Any(t => t.IsActive)) { Status.Text = "请等待浏览器就绪并结束下载后清除。"; return; }
        if (MessageBox.Show(this, "这会退出工具内的网站登录并清除 Cookie、网站存储和缓存。收藏和工具历史列表保留；只想清空历史请使用“历史记录”。继续？", "清除网站数据", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        try { await Browser.CoreWebView2.Profile.ClearBrowsingDataAsync(); _resources.Clear(); Status.Text = "工具内网站数据已清除。"; }
        catch { Status.Text = "清除失败，请关闭网页后重试。"; }
    }

    private async Task RefreshComponentStatus() => ComponentStatus.Text = await _components.GetInstalledAsync(_lifetime.Token) == null ? "视频组件未安装" : "视频组件文件已安装（可检测验证）";
    private async void DetectEnvironment(object sender, RoutedEventArgs e)
    {
        if (_environmentWindow != null || _closing) return;
        try
        {
            _environmentWindow = new EnvironmentWindow(_components) { Owner = this };
            _environmentWindow.ShowDialog();
        }
        catch { Status.Text = "无法打开环境检测窗口，请重新打开网页资源工具后重试。"; return; }
        finally { _environmentWindow = null; }
        if (_forceClose) { Close(); return; }
        try
        {
            await RefreshComponentStatus();
            if (!_browserInitializing && Browser.CoreWebView2 == null)
            {
                // A failed WebView2 initialization may retain its failed task. Retry with a fresh control.
                var border = (Border)Browser.Parent;
                Browser.Dispose();
                UnregisterName("Browser");
                Browser = new Microsoft.Web.WebView2.Wpf.WebView2 { Name = "Browser" };
                RegisterName("Browser", Browser); border.Child = Browser;
                await InitializeBrowserAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch { if (!_closed) Status.Text = "环境状态刷新失败，请重新打开网页资源窗口。"; }
    }
    private async Task<IReadOnlyList<MediaCookie>> CookiesFor(WebResourceRow resource)
    {
        if (Browser.CoreWebView2 == null) return [];
        var cookies = new List<CoreWebView2Cookie>();
        foreach (var url in new[] { resource.Url, resource.PageUrl }.Distinct())
            if (WebResourceRules.IsWebUrl(url)) cookies.AddRange(await Browser.CoreWebView2.CookieManager.GetCookiesAsync(url));
        return cookies.DistinctBy(c => (c.Name, c.Domain, c.Path)).Select(c => new MediaCookie(c.Name, c.Value, c.Domain, c.Path, c.IsSecure,
            c.IsSession ? null : new DateTimeOffset(c.Expires.ToUniversalTime()), !c.Domain.StartsWith('.'))).ToArray();
    }
    private async void InspectPage(object sender, RoutedEventArgs e)
    {
        if (_closing || _closed) return;
        var core = Browser.CoreWebView2;
        if (core == null) { Status.Text = "浏览器尚未就绪。"; return; }
        try { if (await ConfirmPlayerDownload()) return; }
        catch (Exception error) { ReportInspectionFailure(error, InspectionStage.Parsing); return; }
        string source = core.Source;
        var target = PageVideoTargetResolver.Resolve(source);
        if (target.Url == null) { Status.Text = target.Message; return; }
        var row = _resources.FirstOrDefault(r => r.Url == target.Url && r.PageUrl == source && r.IsVideo)
            ?? new WebResourceRow(target.Url, source, WebResourceKind.Video, null);
        if (!_resources.Contains(row)) _resources.Add(row);
        await Track(Inspect(row));
    }
    private async void InspectSelected(object sender, RoutedEventArgs e)
    { if (ResourcesGrid.SelectedItem is WebResourceRow { IsVideo: true } row) await Track(Inspect(row)); }
    private async Task Inspect(WebResourceRow row)
    {
        if (!await ValidatePlayerResource(row)) { Status.Text = StalePlayerResourceMessage; return; }
        if (row.CapturedVideo) { Status.Text = "该视频已按播放器绑定完整媒体，可直接下载 MP4。"; return; }
        var stage = InspectionStage.ReadingCookies;
        try
        {
            Status.Text = "正在解析清晰度…";
            var cookies = await CookiesFor(row);
            stage = InspectionStage.ReadingBrowserIdentity;
            var userAgent = Browser.CoreWebView2?.Settings.UserAgent;
            stage = InspectionStage.Parsing;
            var info = await _downloads.InspectVideoAsync(new Uri(row.Url), _lifetime.Token, cookies, row.PageUrl, userAgent);
            _lifetime.Token.ThrowIfCancellationRequested();
            stage = InspectionStage.UpdatingResult;
            row.Name = info.Title; row.Formats.Clear(); row.Formats.Add(new(null, "最高可用（默认）"));
            foreach (var format in info.Formats) row.Formats.Add(new(format.Id, format.Label));
            row.SelectedFormat = row.Formats[0]; ResourcesGrid.Items.Refresh(); Status.Text = "解析完成，可选择清晰度并下载。";
        }
        catch (Exception error) { ReportInspectionFailure(error, stage); }
    }
    private enum InspectionStage { ReadingCookies, ReadingBrowserIdentity, Parsing, UpdatingResult }
    private void ReportInspectionFailure(Exception error, InspectionStage stage)
    {
        if (_closing || _closed || _lifetime.IsCancellationRequested) return;
        if (error is OperationCanceledException or TimeoutException)
        { Status.Text = "视频解析超时，请稍后重试。"; return; }
        string operation = stage switch
        {
            InspectionStage.ReadingCookies => "读取网站登录信息",
            InspectionStage.ReadingBrowserIdentity => "读取浏览器标识",
            InspectionStage.UpdatingResult => "更新解析结果",
            _ => "解析视频"
        };
        Status.Text = operation + "阶段 · " + FriendlyError(error);
    }
    private void DownloadSelected(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(OutputFolder.Text)) { Status.Text = "请先选择有效的保存目录。"; return; }
        var selected = VisibleResources().Where(row => row.IsSelected).ToArray();
        if (selected.Length == 0) { Status.Text = "请先勾选当前列表中的资源。"; return; }
        foreach (var row in selected)
        {
            if (_tasks.Count(t => t.IsActive) >= 100) { Status.Text = "最多排队 100 个任务。"; break; }
            if (_tasks.Any(t => t.IsActive && t.Resource.Url == row.Url)) continue;
            StartDownload(new WebDownloadRow(row, OutputFolder.Text));
        }
    }
    private void StartDownload(WebDownloadRow task)
    {
        if (_closing || _closed) return;
        if (_tasks.Count(t => t.IsActive) >= 100) { Status.Text = "最多排队 100 个任务。"; return; }
        if (_tasks.Any(t => t.IsActive && t.Resource.Url == task.Resource.Url)) { Status.Text = "该资源已在下载队列中。"; return; }
        _ = Track(Download(task));
    }
    private async Task Track(Task operation)
    {
        _operations.Add(operation);
        try { await operation; }
        finally { _operations.Remove(operation); }
    }
    private async Task Download(WebDownloadRow task)
    {
        _tasks.Add(task);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, task.Cancellation.Token);
        bool entered = false;
        try
        {
            if (!await ValidatePlayerResource(task.Resource))
            { task.Status = StalePlayerResourceMessage; if (!_closing) Status.Text = StalePlayerResourceMessage; return; }
            var cookies = await CookiesFor(task.Resource);
            var userAgent = Browser.CoreWebView2?.Settings.UserAgent;
            await _slots.WaitAsync(linked.Token); entered = true;
            var progress = new Progress<DownloadProgress>(p => { if (!task.IsActive) return; task.Status = p.Status + (p.Detail is null ? "" : " · " + p.Detail); task.Percent = p.Percent; });
            if (task.Resource.CapturedVideo)
                task.OutputPath = await _downloads.DownloadCapturedVideoAsync(new Uri(task.Resource.Url), task.Folder, task.Resource.Name, task.Resource.ExpectedDuration, progress, linked.Token, cookies, task.Resource.PageUrl, userAgent);
            else if (task.Resource.IsVideo)
                task.OutputPath = await _downloads.DownloadVideoAsync(new Uri(task.Resource.Url), task.Folder, task.FormatId, progress, linked.Token, cookies, task.Resource.PageUrl, userAgent, task.Resource.ExpectedDuration);
            else
            {
                var jar = new CookieContainer();
                foreach (var c in cookies)
                {
                    if (c.HostOnly) jar.Add(new Uri((c.Secure ? "https://" : "http://") + c.Domain.TrimStart('.')), new Cookie(c.Name, c.Value, c.Path) { Secure = c.Secure });
                    else jar.Add(new Cookie(c.Name, c.Value, c.Path, c.Domain) { Secure = c.Secure });
                }
                task.OutputPath = await _downloads.DownloadImageAsync(new Uri(task.Resource.Url), task.Folder, task.Resource.Name, progress, linked.Token, jar, task.Resource.PageUrl, userAgent);
            }
            task.Status = "完成"; task.Percent = 100; task.Succeeded = true;
        }
        catch (OperationCanceledException) { task.Status = "已取消"; }
        catch (Exception error) { task.Status = FriendlyError(error); }
        finally { task.IsActive = false; if (entered) _slots.Release(); }
    }
    private static string FriendlyError(Exception error) => WebResourceErrors.Describe(error);
    private void CancelTask(object sender, RoutedEventArgs e) { foreach (var task in TasksGrid.SelectedItems.Cast<WebDownloadRow>()) if (task.IsActive) task.Cancellation.Cancel(); }
    private void RetryTask(object sender, RoutedEventArgs e)
    { foreach (var task in TasksGrid.SelectedItems.Cast<WebDownloadRow>().Where(t => !t.IsActive && !t.Succeeded).ToArray()) StartDownload(new WebDownloadRow(task.Resource, task.Folder)); }
    private void OpenFolder(object sender, RoutedEventArgs e)
    {
        string folder = (TasksGrid.SelectedItem as WebDownloadRow)?.Folder ?? OutputFolder.Text;
        if (Directory.Exists(folder)) Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_environmentWindow != null) { e.Cancel = true; _environmentWindow.RequestCloseForParentExit(); return; }
        if (_closing) { e.Cancel = !_cleanupComplete; return; }
        if (!_forceClose && _operations.Count > 0 && MessageBox.Show(this, "仍有活动任务，关闭会取消它们。确定退出？", "退出网页资源下载", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
        { e.Cancel = true; return; }
        _closing = true; _lifetime.Cancel();
        if (_operations.Count == 0) { _cleanupComplete = true; return; }
        e.Cancel = true; IsEnabled = false;
        try { await Task.WhenAll(_operations.ToArray()); } catch { }
        _cleanupComplete = true;
        Close();
    }
}
