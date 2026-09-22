using System.Collections.ObjectModel;
using System.Windows.Media;
using ToolsBox.App.Infrastructure;
using ToolsBox.Core.WebResources;

namespace ToolsBox.App.WebResources;

public sealed record ResourceFormat(string? Id, string Label);

public sealed class WebResourceRow : ObservableObject
{
    private string _name;
    private ImageSource? _thumbnail;
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public WebResourceRow(string url, string pageUrl, WebResourceKind kind, long? size)
    {
        Url = url; PageUrl = pageUrl; Kind = kind; Size = size;
        _name = Uri.UnescapeDataString(new Uri(url).AbsolutePath.Split('/').LastOrDefault() ?? "资源");
        if (string.IsNullOrWhiteSpace(_name)) _name = "资源";
        Formats.Add(new(null, "最高可用（默认）")); SelectedFormat = Formats[0];
    }
    public string Url { get; }
    public string PageUrl { get; }
    public WebResourceKind Kind { get; }
    public long? Size { get; }
    public bool CapturedVideo { get; init; }
    public double? ExpectedDuration { get; init; }
    internal PlayerResourceBinding? PlayerBinding { get; init; }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Host => new Uri(Url).Host;
    public string PageHost => Uri.TryCreate(PageUrl, UriKind.Absolute, out var uri) ? uri.Host : "";
    public bool IsVideo => Kind != WebResourceKind.Image;
    public string KindLabel => Kind switch { WebResourceKind.Image => "图片", WebResourceKind.Hls => "HLS 流", WebResourceKind.Dash => "DASH 流", _ => "视频" };
    public string SizeLabel => Size is > 0 ? $"{Size / 1024d / 1024d:F1} MB" : "未知";
    public ObservableCollection<ResourceFormat> Formats { get; } = [];
    public ResourceFormat SelectedFormat { get; set; }
    public ImageSource? Thumbnail { get => _thumbnail; set => SetProperty(ref _thumbnail, value); }
}

public sealed class WebDownloadRow : ObservableObject
{
    private string _status = "排队中";
    private double? _percent;
    public WebDownloadRow(WebResourceRow resource, string folder)
    { Resource = resource; Folder = folder; FormatId = resource.SelectedFormat.Id; }
    public WebResourceRow Resource { get; }
    public string Folder { get; }
    public string? FormatId { get; }
    public string Name => Resource.Name;
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public double? Percent { get => _percent; set { if (SetProperty(ref _percent, value)) OnPropertyChanged(nameof(PercentText)); } }
    public string PercentText => Percent is { } p ? $"{p:F1}%" : "—";
    public CancellationTokenSource Cancellation { get; } = new();
    public bool IsActive { get; set; } = true;
    public bool Succeeded { get; set; }
    public string? OutputPath { get; set; }
}
