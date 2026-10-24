using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ToolsBox.App.Views;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class OvertimeHistoryViewTests
{
    [Fact]
    public Task History_SeparatesMonthlyRecordsFromSettingsAndAttendance() => WpfTestThread.RunAsync(async () =>
    {
        using var host = new HistoryHost();
        await host.LayoutAsync();
        var history = host.Find<Border>("OvertimeHistoryCard");
        var settings = host.Find<Border>("CountdownSettingsCard");
        var parent = Assert.IsType<StackPanel>(VisualTreeHelper.GetParent(history));
        int historyIndex = parent.Children.IndexOf(history);
        Assert.Equal(parent.Children.IndexOf(settings) + 1, historyIndex);
        Assert.Equal("AttendancePanel", parent.Children[historyIndex + 1].GetType().Name);

        Assert.Equal(host.Model.OvertimeMonthText, host.Find<TextBlock>("OvertimeHistoryMonth").Text);
        Assert.Equal(host.Model.OvertimeSummaryText, host.Find<TextBlock>("OvertimeHistorySummary").Text);
        Assert.Same(host.Model.PreviousOvertimeMonthCommand, host.Find<Button>("PreviousOvertimeMonthButton").Command);
        Assert.Same(host.Model.NextOvertimeMonthCommand, host.Find<Button>("NextOvertimeMonthButton").Command);
        Assert.Same(host.Model.CurrentOvertimeMonthCommand, host.Find<Button>("CurrentOvertimeMonthButton").Command);
        Assert.Same(host.Model.RefreshOvertimeHistoryCommand, host.Find<Button>("RefreshOvertimeHistoryButton").Command);
        host.Find<Button>("RefreshOvertimeHistoryButton").Command.Execute(null);
        Assert.Equal(1, host.Model.RefreshCount);

        var grid = host.Find<DataGrid>("OvertimeHistoryGrid");
        Assert.Same(host.Model.OvertimeRecords, grid.ItemsSource);
        Assert.True(grid.IsReadOnly);
        Assert.False(grid.AutoGenerateColumns);
        Assert.False(grid.CanUserAddRows);
        Assert.False(grid.CanUserDeleteRows);
        Assert.Equal(new[] { "日期", "类型", "上班", "实际下班", "实际加班" }, grid.Columns.Select(column => column.Header));
        Assert.Equal(new[] { "DateText", "DayTypeText", "StartedAtText", "FinishedAtText", "DurationText" },
            grid.Columns.Cast<DataGridTextColumn>().Select(column => Assert.IsType<Binding>(column.Binding).Path.Path));
        Assert.InRange(grid.MinHeight, 110, 160);
        Assert.InRange(grid.MaxHeight, 240, 280);
        Assert.True(grid.EnableRowVirtualization && grid.EnableColumnVirtualization);
    });

    [Fact]
    public Task History_ShowsEmptyMonthAndRecoverableStorageError() => WpfTestThread.RunAsync(async () =>
    {
        using var host = new HistoryHost();
        host.Model.SetEmptyMonth();
        await host.LayoutAsync();
        Assert.Equal(Visibility.Collapsed, host.Find<DataGrid>("OvertimeHistoryGrid").Visibility);
        Assert.Equal(Visibility.Visible, host.Find<TextBlock>("OvertimeHistoryEmpty").Visibility);
        Assert.Contains("暂无下班记录", host.Find<TextBlock>("OvertimeHistoryEmpty").Text);
        Assert.Equal("月合计：0 天 · 0 小时 0 分钟", host.Find<TextBlock>("OvertimeHistorySummary").Text);
        var error = host.Find<TextBlock>("OvertimeHistoryError");
        Assert.Equal(Visibility.Collapsed, error.Visibility);
        host.Model.OvertimeHistoryError = "保存失败，请刷新重试。";
        await host.LayoutAsync();
        Assert.Equal(Visibility.Visible, error.Visibility);
        Assert.Equal(host.Model.OvertimeHistoryError, error.Text);
        Assert.True(host.Find<Button>("RefreshOvertimeHistoryButton").IsEnabled);
        host.Model.OvertimeHistoryError = "";
        await host.LayoutAsync();
        Assert.Equal(Visibility.Collapsed, error.Visibility);
    });

    [Theory]
    [InlineData(640, false)]
    [InlineData(640, true)]
    [InlineData(1060, false)]
    [InlineData(1060, true)]
    public Task History_UsesLiveThemeAndComfortGridAtNarrowAndWideWidths(int width, bool dark) => WpfTestThread.RunAsync(async () =>
    {
        using var host = new HistoryHost(width);
        var history = host.Find<Border>("OvertimeHistoryCard");
        var grid = host.Find<DataGrid>("OvertimeHistoryGrid");
        foreach (bool theme in new[] { dark, !dark, dark })
        {
            host.ApplyTheme(theme);
            await host.LayoutAsync();
            Assert.Equal(host.Brush("ToolboxSurfaceBrush").Color, Assert.IsType<SolidColorBrush>(history.Background).Color);
            Assert.Equal(host.Brush("ToolboxBorderBrush").Color, Assert.IsType<SolidColorBrush>(history.BorderBrush).Color);
            Assert.Equal(host.Brush("ToolboxTextBrush").Color, Assert.IsType<SolidColorBrush>(grid.Foreground).Color);
            Assert.Equal(host.Brush("ToolboxTextBrush").Color,
                Assert.IsType<SolidColorBrush>(host.Find<TextBlock>("OvertimeHistoryMonth").Foreground).Color);
            Assert.Equal(host.Brush("ToolboxTextBrush").Color,
                Assert.IsType<SolidColorBrush>(Assert.Single(Descendants<TextBlock>(history), text => text.Text == "加班记录").Foreground).Color);
            Assert.True(history.ActualWidth <= width - 56);
            Assert.True(grid.ActualWidth <= history.ActualWidth - history.Padding.Left - history.Padding.Right);
            Assert.InRange(grid.ActualHeight, grid.MinHeight, grid.MaxHeight);
            Assert.Equal(42, grid.RowHeight);
            Assert.Equal(40, grid.ColumnHeaderHeight);
            Assert.Equal(Visibility.Visible, grid.Visibility);
            Assert.Equal(Visibility.Collapsed, host.Find<TextBlock>("OvertimeHistoryEmpty").Visibility);
            foreach (Button button in Descendants<Button>(history))
            {
                Point position = button.TranslatePoint(new Point(), history);
                Assert.True(position.X >= 0 && position.X + button.ActualWidth <= history.ActualWidth + 1,
                    $"Month control {button.Name} must remain within the record area at width {width}.");
            }
            grid.ApplyTemplate();
            DataGridCell[] cells = Descendants<DataGridCell>(grid).ToArray();
            Assert.NotEmpty(cells);
            foreach (DataGridCell cell in cells)
                Assert.Equal(VerticalAlignment.Center, cell.VerticalContentAlignment);
            Assert.Contains(Descendants<TextBlock>(grid), text => text.Text == "10-25 02:15");
            Assert.Contains(Descendants<TextBlock>(grid), text => text.Text == "100小时30分20秒");
            Assert.DoesNotContain(Descendants<ScrollBar>(grid), candidate =>
                candidate.Orientation == Orientation.Horizontal && candidate.Visibility == Visibility.Visible);
            var bar = Descendants<ScrollBar>(grid).First(candidate => candidate.Orientation == Orientation.Vertical);
            Assert.Equal(12, bar.Width);
            Assert.NotNull(bar.Template);
        }
        SaveHistorySnapshot(history, host.View, width, dark);
    });

    private static void SaveHistorySnapshot(FrameworkElement history, Control source, int width, bool dark)
    {
        string directory = Path.Combine(RepositoryRoot(), "TestResults", "overtime-history-ui");
        Directory.CreateDirectory(directory);
        var parent = Assert.IsAssignableFrom<Panel>(VisualTreeHelper.GetParent(history));
        int index = parent.Children.IndexOf(history);
        Thickness margin = history.Margin;
        double captureWidth = history.ActualWidth;
        double captureHeight = history.ActualHeight;
        FontFamily fontFamily = source.FontFamily;
        double fontSize = source.FontSize;
        Brush foreground = source.Foreground;
        var root = new Grid { DataContext = history.DataContext, Resources = source.Resources, UseLayoutRounding = true };
        System.Windows.Documents.TextElement.SetFontFamily(root, fontFamily);
        System.Windows.Documents.TextElement.SetFontSize(root, fontSize);
        System.Windows.Documents.TextElement.SetForeground(root, foreground);
        parent.Children.Remove(history);
        try
        {
            history.Margin = new Thickness();
            root.Children.Add(history);
            root.Measure(new Size(captureWidth, captureHeight));
            root.Arrange(new Rect(0, 0, captureWidth, captureHeight));
            root.UpdateLayout();
            Assert.False(root.IsLoaded);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(captureWidth), (int)Math.Ceiling(captureHeight),
                96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            int opaquePixels = 0;
            bool hasVisibleColor = false;
            for (int offset = 0; offset < pixels.Length; offset += 4)
            {
                if (pixels[offset + 3] > 0) opaquePixels++;
                if (pixels[offset] > 16 || pixels[offset + 1] > 16 || pixels[offset + 2] > 16) hasVisibleColor = true;
            }
            Assert.True(opaquePixels > bitmap.PixelWidth * bitmap.PixelHeight / 2 && hasVisibleColor,
                "The history snapshot must contain an opaque record surface and visible theme/text pixels; a transparent or black image is not layout evidence.");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(directory, $"history-{width}-{(dark ? "dark" : "light")}.png"));
            encoder.Save(output);
        }
        finally
        {
            root.Children.Remove(history);
            history.Margin = margin;
            parent.Children.Insert(index, history);
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "ToolsBox.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("ToolsBox repository was not found.");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed class HistoryHost : IDisposable
    {
        private readonly int _width;
        private readonly Window _window;
        public HistoryHost(int width = 760)
        {
            _width = width;
            View = new WorkCountdownView { DataContext = Model };
            View.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/宝哥工具箱;component/Themes/ComfortControls.xaml", UriKind.Relative)
            });
            _window = new Window
            {
                Content = View, Resources = View.Resources, ShowActivated = false, ShowInTaskbar = false,
                FontFamily = new FontFamily("Segoe UI, Microsoft YaHei"), FontSize = 13, UseLayoutRounding = true
            };
            ApplyTheme(false);
        }

        public HistoryModel Model { get; } = new();
        public WorkCountdownView View { get; }
        public T Find<T>(string name) where T : class => Assert.IsType<T>(View.FindName(name));
        public SolidColorBrush Brush(string key) => Assert.IsType<SolidColorBrush>(_window.Resources[key]);
        public void ApplyTheme(bool dark) => typeof(App).Assembly.GetType("ToolsBox.App.Infrastructure.ComfortAppearance", true)!
            .GetMethod("Apply", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [_window, dark]);
        public async Task LayoutAsync()
        {
            View.Measure(new Size(_width, 900));
            View.Arrange(new Rect(0, 0, _width, 900));
            View.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            View.UpdateLayout();
            Assert.False(_window.IsVisible);
            Assert.False(View.IsLoaded);
        }
        public void Dispose() => _window.Close();
    }

    public sealed class HistoryModel : INotifyPropertyChanged
    {
        private string _error = "";
        public HistoryModel() => RefreshOvertimeHistoryCommand = new TestCommand(() => RefreshCount++);
        public ObservableCollection<HistoryRow> OvertimeRecords { get; } = new(Enumerable.Range(1, 12)
            .Select(day => new HistoryRow($"2026-10-{day:00}", "工作日", "08:30", "10-25 02:15", "100小时30分20秒")));
        public string OvertimeMonthText => "2026年10月";
        public string OvertimeSummaryText => HasOvertimeRecords ? "月合计：12 天 · 27 小时 0 分钟" : "月合计：0 天 · 0 小时 0 分钟";
        public bool HasOvertimeRecords => OvertimeRecords.Count > 0;
        public bool HasNoOvertimeRecords => !HasOvertimeRecords;
        public string OvertimeHistoryError { get => _error; set { _error = value; OnChanged(); } }
        public ICommand PreviousOvertimeMonthCommand { get; } = new TestCommand();
        public ICommand NextOvertimeMonthCommand { get; } = new TestCommand();
        public ICommand CurrentOvertimeMonthCommand { get; } = new TestCommand();
        public ICommand RefreshOvertimeHistoryCommand { get; }
        public int RefreshCount { get; private set; }
        public void SetEmptyMonth()
        {
            OvertimeRecords.Clear();
            OnChanged(nameof(HasOvertimeRecords));
            OnChanged(nameof(HasNoOvertimeRecords));
            OnChanged(nameof(OvertimeSummaryText));
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    }

    public sealed record HistoryRow(string DateText, string DayTypeText, string StartedAtText, string FinishedAtText, string DurationText);

    private sealed class TestCommand(Action? action = null) : ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action?.Invoke();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
