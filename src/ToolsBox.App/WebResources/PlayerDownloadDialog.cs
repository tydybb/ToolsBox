using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ToolsBox.Core.WebResources;

namespace ToolsBox.App.WebResources;

internal sealed record PlayerDownloadCandidate(int FrameId, long FrameGeneration, PlayerDocument Document, PlayerSnapshot Player, BoundPlayerMedia Media)
{
    public string Label => Media.Title + " · " + (Media.Duration is { } d ? TimeSpan.FromSeconds(d).ToString(@"hh\:mm\:ss") : "时长未知");
}

internal sealed class PlayerDownloadDialog : Window
{
    private readonly ComboBox _players = new() { DisplayMemberPath = nameof(PlayerDownloadCandidate.Label), Margin = new(0, 6, 0, 12) };
    private readonly ComboBox _quality = new() { DisplayMemberPath = nameof(PlayerMediaChoice.Label), Margin = new(0, 6, 0, 12) };
    private readonly TextBox _folder = new() { Margin = new(0, 6, 0, 8) };
    private readonly TextBlock _source = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
    public PlayerDownloadCandidate Candidate => (PlayerDownloadCandidate)_players.SelectedItem;
    public PlayerMediaChoice Choice => (PlayerMediaChoice)_quality.SelectedItem;
    public string Folder => _folder.Text.Trim();

    public PlayerDownloadDialog(IReadOnlyList<PlayerDownloadCandidate> candidates, string folder)
    {
        Title = "宝哥工具箱 · 确认下载视频"; Width = 560; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new(22) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "选择要下载的播放器（仅下载你有权保存的内容）", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_players); panel.Children.Add(_source);
        panel.Children.Add(new TextBlock { Text = "清晰度 / 媒体来源" }); panel.Children.Add(_quality);
        panel.Children.Add(new TextBlock { Text = "保存目录" }); panel.Children.Add(_folder);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var browse = new Button { Content = "选择目录", Padding = new(12, 6, 12, 6), Margin = new(4) };
        var cancel = new Button { Content = "取消", IsCancel = true, Padding = new(12, 6, 12, 6), Margin = new(4) };
        var confirm = new Button { Content = "确认下载 MP4", Padding = new(12, 6, 12, 6), Margin = new(4) };
        // Not IsDefault: a webpage key press must not accidentally accept this native prompt.
        browse.Click += (_, _) => { var picker = new OpenFolderDialog(); if (picker.ShowDialog(this) == true) _folder.Text = picker.FolderName; };
        confirm.Click += (_, _) =>
        {
            if (_players.SelectedItem == null || _quality.SelectedItem == null || !Directory.Exists(Folder))
            { MessageBox.Show(this, "请选择视频、清晰度和有效的保存目录。"); return; }
            DialogResult = true;
        };
        buttons.Children.Add(browse); buttons.Children.Add(cancel); buttons.Children.Add(confirm); panel.Children.Add(buttons);
        _players.SelectionChanged += (_, _) =>
        {
            if (_players.SelectedItem is not PlayerDownloadCandidate candidate) return;
            _source.Text = "网页来源：" + new Uri(candidate.Document.Url).Host + "\n视频编号：" + (candidate.Player.SiteId.Length > 0 ? candidate.Player.SiteId : "当前播放器");
            _quality.ItemsSource = candidate.Media.Choices; _quality.SelectedIndex = 0;
        };
        _folder.Text = folder; _players.ItemsSource = candidates; _players.SelectedIndex = 0;
    }
}
