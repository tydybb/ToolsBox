using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ToolsBox.App.Attendance;
using ToolsBox.App.Lottery;
using ToolsBox.App.NetworkTraffic;
using ToolsBox.App.Views;
using ToolsBox.App.WorkCountdown;
using ToolsBox.Core.Lottery;
using ToolsBox.Core.NetworkTraffic;
using ToolsBox.Core.Ports;

namespace ToolsBox.App.Tests.Startup;

public sealed class ComfortLayoutTests
{
    private static readonly (string Name, string Label, string File)[] Pages =
    [
        ("PortNavigation", "端口监控", "ports"),
        ("FileNavigation", "文件解除占用", "files"),
        ("NetworkNavigation", "网络流量", "network"),
        ("CountdownNavigation", "下班倒计时", "countdown"),
        ("WebResourcesNavigation", "网页资源下载", "web"),
        ("LotteryNavigation", "双色球统计", "lottery")
    ];

    [Theory]
    [InlineData(false, "light")]
    [InlineData(true, "dark")]
    public Task SixPages_RenderReadableAtNormalAndMinimumContentSizes(bool dark, string theme) =>
        WpfTestThread.RunAsync(async () =>
        {
            using var fixture = new Fixture();
            fixture.ApplyAppearance(dark);
            for (int selected = 0; selected < Pages.Length; selected++)
            {
                var page = Pages[selected];
                var view = await fixture.SelectPageAsync(page.Name);
                var heading = Descendants<TextBlock>(view).Single(x => x.Text == page.Label && x.FontSize >= 24);
                Assert.Equal(PaletteColor(fixture.Window, "Text"), BrushColor(heading.Foreground));
                Assert.True(heading.ActualWidth > 0 && heading.ActualHeight >= 24);
                AssertNavigation(fixture, selected);
                AssertReadableText(fixture.ContentRoot, BrushColor(fixture.Window.Background));
                if (selected == 0)
                {
                    var grid = Assert.IsType<DataGrid>(view.FindName("PortGrid"));
                    Assert.Equal(2, grid.Items.Count);
                    Assert.Equal(5, grid.Columns.Count);
                    foreach (var entry in fixture.Model.PortMonitor.Entries)
                    {
                        var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromItem(entry));
                        var texts = Descendants<TextBlock>(row).Select(x => x.Text).ToArray();
                        Assert.Contains(entry.ProcessName, texts);
                        Assert.Contains(entry.ProcessId.ToString(), texts);
                        Assert.Contains(entry.LocalAddress, texts);
                        Assert.Contains(entry.LocalPort.ToString(), texts);
                        Assert.Contains(entry.AddressFamilyDisplay, texts);
                    }
                }
                WpfTestSnapshot.SaveWindowContent(fixture.Window, 1280, 760, $"comfort-{page.File}-{theme}.png");
                // 主窗口最小宽 980 减去导航栏 220，实际工具内容区为 760 × 580。
                await fixture.LayoutAsync(980, 580);
                var host = fixture.ContentRoot.Children.OfType<ContentControl>().Single();
                Assert.InRange(host.ActualWidth, 759.5, 760.5);
                Assert.InRange(host.ActualHeight, 579.5, 580.5);
                AssertNavigation(fixture, selected);
                AssertReadableText(fixture.ContentRoot, BrushColor(fixture.Window.Background));
                if (selected is 1 or 2)
                {
                    var table = Descendants<DataGrid>(view).First();
                    foreach (var column in table.Columns.Where(column => column.Width.IsAbsolute))
                        Assert.True(column.ActualWidth >= column.Width.Value - 0.5,
                            $"{page.Label} 列 {column.Header} 被压缩为 {column.ActualWidth:0.##}，原宽 {column.Width.Value:0.##}，应改为横向滚动避免截断");
                    Assert.Equal(Visibility.Visible, Descendants<ScrollViewer>(table).First().ComputedHorizontalScrollBarVisibility);
                }
                WpfTestSnapshot.SaveWindowContent(fixture.Window, 980, 580, $"comfort-{page.File}-{theme}-minimum.png");
            }
        });

    [Fact]
    public Task ThemeSwitch_UpdatesExistingNavigationInputsHeadersAndRows() =>
        WpfTestThread.RunAsync(async () =>
        {
            using var fixture = new Fixture();
            var view = await fixture.SelectPageAsync("PortNavigation");
            var search = Assert.IsType<TextBox>(view.FindName("PortSearchInput"));
            var grid = Assert.IsType<DataGrid>(view.FindName("PortGrid"));
            var selected = fixture.Model.PortMonitor.Entries[0];
            grid.SelectedItem = selected;
            var navigation = Assert.IsAssignableFrom<ToggleButton>(fixture.Window.FindName("PortNavigation"));
            Color? previousForeground = null;
            foreach (bool dark in new[] { false, true, false })
            {
                fixture.ApplyAppearance(dark);
                await fixture.LayoutAsync(1280, 760);
                // WPF 在主题资源更换时可回收行容器；检验当前显示行，而不是已脱离树的旧引用。
                var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromItem(selected));
                var header = Descendants<DataGridColumnHeader>(grid).First(x => x.Column == grid.Columns[0]);
                Assert.Equal(PaletteColor(fixture.Window, "Background"), BrushColor(fixture.Window.Background));
                Assert.Equal(PaletteColor(fixture.Window, "Text"), BrushColor(search.Foreground));
                Assert.Equal(PaletteColor(fixture.Window, "Surface"), BrushColor(search.Background));
                Assert.Equal(PaletteColor(fixture.Window, "Text"), BrushColor(row.Foreground));
                Assert.Equal(PaletteColor(fixture.Window, "Selection"), BrushColor(row.Background));
                Assert.Equal(PaletteColor(fixture.Window, "Muted"), BrushColor(header.Foreground));
                Assert.Equal(PaletteColor(fixture.Window, "Hover"), BrushColor(header.Background));
                Assert.Equal(PaletteColor(fixture.Window, "Accent"), BrushColor(navigation.Foreground));
                Assert.Equal(PaletteColor(fixture.Window, "Surface"), BrushColor(navigation.Background));
                if (previousForeground is { } previous) Assert.NotEqual(previous, BrushColor(search.Foreground));
                previousForeground = BrushColor(search.Foreground);
                Assert.Same(selected, grid.SelectedItem);
                Assert.Same(selected, row.Item);
                AssertReadableText(fixture.ContentRoot, BrushColor(fixture.Window.Background));
            }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ComboBoxes_ExpandRealPopupsShowLabelsAndUpdateSelection(bool dark) =>
        WpfTestThread.RunAsync(async () =>
        {
            using var fixture = new Fixture();
            fixture.ApplyAppearance(dark);
            // 只有普通测试宿主 Show；主窗口不显示，不触发其 OnLoaded 的生产初始化。
            fixture.ShowContentHost();
            var ports = await fixture.SelectPageAsync("PortNavigation");
            var portGrid = Assert.IsType<DataGrid>(ports.FindName("PortGrid"));
            var menu = Assert.IsType<ContextMenu>(portGrid.ContextMenu);
            menu.PlacementTarget = portGrid;
            menu.IsOpen = true;
            try
            {
                foreach (bool nextDark in new[] { dark, !dark, dark })
                {
                    fixture.ApplyAppearance(nextDark);
                    await fixture.LayoutAsync(1280, 760);
                    Assert.True(menu.IsOpen);
                    Assert.True(menu.ActualWidth > 0 && menu.ActualHeight > 0);
                    Assert.Equal(PaletteColor(fixture.Window, "Surface"), BrushColor(menu.Background));
                    Assert.Equal(PaletteColor(fixture.Window, "Text"), BrushColor(menu.Foreground));
                    foreach (var item in menu.Items.OfType<MenuItem>())
                    {
                        Assert.True(item.ActualHeight > 0);
                        Assert.Equal(PaletteColor(fixture.Window, "Text"), BrushColor(item.Foreground));
                    }
                    AssertReadableText(menu, BrushColor(menu.Background));
                }
            }
            finally
            {
                menu.IsOpen = false;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }
            var interval = Assert.IsType<ComboBox>(ports.FindName("PortRefreshInterval"));
            Assert.Equal(new[] { 1, 2, 5, 10 }, interval.Items.Cast<int>().ToArray());
            await ExerciseComboAsync(fixture, interval, ["1 秒", "2 秒", "5 秒", "10 秒"], dark,
                index => Assert.Equal((int)interval.Items[index], fixture.Model.PortMonitor.RefreshIntervalSeconds));
            var countdown = await fixture.SelectPageAsync("CountdownNavigation");
            var day = Assert.IsType<ComboBox>(countdown.FindName("DayModeInput"));
            string[] labels = fixture.Model.WorkCountdown.DayChoices.Select(x => x.Label).ToArray();
            Assert.Equal(3, day.Items.Count);
            await ExerciseComboAsync(fixture, day, labels, dark,
                index => Assert.Equal(fixture.Model.WorkCountdown.DayChoices[index].Mode, fixture.Model.WorkCountdown.DayMode));
        });

    [Fact]
    public Task PortGroupedColumns_CopyValuesPreserveAllNineFieldsWithoutSystemClipboard() =>
        WpfTestThread.RunAsync(async () =>
        {
            using var fixture = new Fixture();
            var view = await fixture.SelectPageAsync("PortNavigation");
            var grid = Assert.IsType<DataGrid>(view.FindName("PortGrid"));
            foreach (var entry in fixture.Model.PortMonitor.Entries)
            {
                string copied = string.Join("\t", grid.Columns.Select(column =>
                    Assert.IsType<string>(column.OnCopyingCellClipboardContent(entry))));
                foreach (string value in new[] { entry.ProcessName, entry.ProcessId.ToString(), entry.Protocol,
                    entry.AddressFamilyDisplay, entry.LocalAddress, entry.LocalPort.ToString(), entry.RemoteAddress,
                    entry.RemotePort?.ToString() ?? "—", entry.State })
                    Assert.Contains(value, copied);
            }
        });

    [Fact]
    public Task PortGroupedColumns_ContextMenuSortsAllNineFieldsInBothDirections() =>
        WpfTestThread.RunAsync(async () =>
        {
            using var fixture = new Fixture();
            var view = await fixture.SelectPageAsync("PortNavigation");
            var grid = Assert.IsType<DataGrid>(view.FindName("PortGrid"));
            var menu = Assert.IsType<ContextMenu>(grid.ContextMenu);
            string[] fields = ["ProcessName", "ProcessId", "Protocol", "AddressFamilyDisplay",
                "LocalAddress", "LocalPort", "RemoteAddress", "RemotePort", "State"];
            var items = menu.Items.OfType<MenuItem>().ToArray();
            Assert.Equal(fields.OrderBy(x => x), items.Select(x => Assert.IsType<string>(x.Tag)).OrderBy(x => x));
            foreach (string field in fields)
            {
                var item = items.Single(x => Equals(x.Tag, field));
                var property = typeof(PortEntry).GetProperty(field)!;
                foreach (var direction in new[] { ListSortDirection.Ascending, ListSortDirection.Descending })
                {
                    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    var sorting = Assert.Single(CollectionViewSource.GetDefaultView(grid.ItemsSource).SortDescriptions);
                    Assert.Equal(field, sorting.PropertyName);
                    Assert.Equal(direction, sorting.Direction);
                    IEnumerable<PortEntry> expected = fixture.Model.PortMonitor.Entries
                        .OrderBy(x => property.GetValue(x), Comparer<object?>.Default);
                    if (direction == ListSortDirection.Descending) expected = expected.Reverse();
                    Assert.Equal(expected.ToArray(), grid.Items.Cast<PortEntry>().ToArray());
                }
            }
        });

    [Fact]
    public Task NetworkExpandButton_FitsItsCellAndClickRevealsProcessDetails() =>
        WpfTestThread.RunAsync(async () =>
        {
            using var fixture = new Fixture();
            var view = await fixture.SelectPageAsync("NetworkNavigation");
            var grid = Descendants<DataGrid>(view).Single(x => ReferenceEquals(x.ItemsSource, fixture.Model.NetworkTraffic.Items));
            var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
            var expand = Descendants<Button>(row).Single(x => Equals(x.ToolTip, "展开或收起进程"));
            var cell = Ancestor<DataGridCell>(expand);
            Rect buttonBounds = expand.TransformToAncestor(cell).TransformBounds(new Rect(expand.RenderSize));
            Assert.True(buttonBounds.Left >= -0.5 && buttonBounds.Right <= cell.ActualWidth + 0.5,
                $"展开按钮 {buttonBounds} 超出单元格宽度 {cell.ActualWidth:0.##}");
            Assert.True(buttonBounds.Top >= -0.5 && buttonBounds.Bottom <= cell.ActualHeight + 0.5,
                $"展开按钮 {buttonBounds} 超出单元格高度 {cell.ActualHeight:0.##}");
            Assert.False(fixture.NetworkItem.IsExpanded);
            expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await fixture.LayoutAsync(1280, 760);
            Assert.True(fixture.NetworkItem.IsExpanded);
            Assert.Equal(Visibility.Visible, row.DetailsVisibility);
            var details = Descendants<DataGrid>(row).Single(x => ReferenceEquals(x.ItemsSource, fixture.NetworkItem.Processes));
            Assert.Single(details.Items.Cast<ProcessTrafficItemViewModel>());
            Assert.True(details.ActualHeight > 0);
            expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await fixture.LayoutAsync(1280, 760);
            Assert.False(fixture.NetworkItem.IsExpanded);
            Assert.Equal(Visibility.Collapsed, row.DetailsVisibility);
        });

    private static async Task ExerciseComboAsync(Fixture fixture, ComboBox combo, string[] labels,
        bool dark, Action<int> assertSource)
    {
        combo.ApplyTemplate();
        var selection = Assert.IsType<ContentPresenter>(combo.Template.FindName("SelectionContent", combo));
        var popup = Assert.IsType<Popup>(combo.Template.FindName("PART_Popup", combo));
        combo.IsDropDownOpen = true;
        try
        {
            await fixture.LayoutAsync(1280, 760);
            Assert.True(combo.IsDropDownOpen);
            Assert.True(popup.IsOpen);
            var border = Assert.IsType<Border>(popup.Child);
            Assert.True(border.ActualWidth >= combo.ActualWidth - 1);
            for (int index = 0; index < labels.Length; index++)
            {
                var item = Assert.IsType<ComboBoxItem>(combo.ItemContainerGenerator.ContainerFromIndex(index));
                Assert.Contains(Descendants<TextBlock>(item), text => text.Text == labels[index] && text.ActualWidth > 0);
                Assert.Equal(PaletteColor(fixture.Window, "Text"), BrushColor(item.Foreground));
                combo.SelectedIndex = index;
                await fixture.LayoutAsync(1280, 760);
                Assert.Contains(Descendants<TextBlock>(selection), text => text.Text == labels[index] && text.ActualWidth > 0);
                assertSource(index);
            }
            // 展开的 Popup 也必须响应现有窗口资源变化，且保留当前选择。
            foreach (bool nextDark in new[] { !dark, dark })
            {
                fixture.ApplyAppearance(nextDark);
                await fixture.LayoutAsync(1280, 760);
                Assert.True(popup.IsOpen);
                Assert.Equal(PaletteColor(fixture.Window, "Surface"), BrushColor(border.Background));
                Assert.Equal(PaletteColor(fixture.Window, "Text"), BrushColor(combo.Foreground));
                Assert.Equal(labels.Length - 1, combo.SelectedIndex);
                AssertReadableText(border, BrushColor(border.Background));
            }
        }
        finally
        {
            combo.IsDropDownOpen = false;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
        Assert.False(popup.IsOpen);
    }

    private static void AssertNavigation(Fixture fixture, int selected)
    {
        var bounds = new Rect(fixture.ContentRoot.RenderSize);
        for (int index = 0; index < Pages.Length; index++)
        {
            var navigation = Assert.IsAssignableFrom<ToggleButton>(fixture.Window.FindName(Pages[index].Name));
            Assert.Equal(index == selected, navigation.IsChecked);
            var label = Assert.IsType<StackPanel>(navigation.Content).Children.OfType<TextBlock>().Last();
            Assert.Equal(Pages[index].Label, label.Text);
            Rect rectangle = label.TransformToAncestor(fixture.ContentRoot).TransformBounds(new Rect(label.RenderSize));
            Assert.True(bounds.Contains(rectangle), $"导航文字 {label.Text} 被裁切：{rectangle}，内容区 {bounds}");
        }
    }

    private static Color PaletteColor(Window window, string name) => BrushColor((Brush)window.Resources["Toolbox" + name + "Brush"]);
    private static Color BrushColor(Brush brush) => Assert.IsType<SolidColorBrush>(brush).Color;

    private static void AssertReadableText(FrameworkElement root, Color fallback)
    {
        var viewport = new Rect(root.RenderSize);
        foreach (var text in Descendants<TextBlock>(root))
        {
            if (string.IsNullOrWhiteSpace(text.Text) || text.ActualWidth <= 0 || text.ActualHeight <= 0
                || text.FontFamily.Source.Contains("MDL2", StringComparison.OrdinalIgnoreCase)) continue;
            Rect bounds = text.TransformToAncestor(root).TransformBounds(new Rect(text.RenderSize));
            if (!viewport.IntersectsWith(bounds)) continue;
            Color background = fallback;
            bool foundBackground = false, skip = false;
            for (DependencyObject? current = text; current is not null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is UIElement element && (element.Visibility != Visibility.Visible || element.Opacity <= 0 || !element.IsEnabled))
                { skip = true; break; }
                Brush? brush = current switch
                {
                    TextBlock block => block.Background,
                    Border border => border.Background,
                    Panel panel => panel.Background,
                    Control control => control.Background,
                    _ => null
                };
                if (!foundBackground && brush is SolidColorBrush solid && solid.Color.A == 255)
                { background = solid.Color; foundBackground = true; }
                if (ReferenceEquals(current, root)) break;
            }
            if (skip) continue;
            double foregroundLuminance = Luminance(BrushColor(text.Foreground));
            double backgroundLuminance = Luminance(background);
            double contrast = (Math.Max(foregroundLuminance, backgroundLuminance) + 0.05)
                / (Math.Min(foregroundLuminance, backgroundLuminance) + 0.05);
            Assert.True(contrast >= 3, $"文字“{text.Text}”与背景对比度只有 {contrast:0.00}：{text.Foreground} / {background}");
        }
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte value)
        {
            double channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static T Ancestor<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? current = VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        throw new InvalidOperationException($"未找到 {typeof(T).Name} 父元素。");
    }

    private sealed class Fixture : IDisposable
    {
        // 只使用唯一测试目录的账本和打卡设置；不创建 Application，也不显示主窗口。
        private readonly string _root = Path.Combine(AppContext.BaseDirectory, "comfort-fixture-" + Guid.NewGuid().ToString("N"));
        private readonly HashSet<AttendancePanel> _isolatedAttendancePanels = [];
        private Window? _contentHost;
        private readonly WorkCountdownViewModel _countdown = new(
            () => new DateTime(2026, 10, 8, 10, 0, 0), new EmptyCountdownStore(), false);

        public Fixture()
        {
            Window = new MainWindow(_countdown, _ => { }, _ => { });
            ContentRoot = Assert.IsType<Grid>(Window.Content);
            Model = Assert.IsType<MainViewModel>(Window.DataContext);
            Model.PortMonitor.IsPaused = true;
            Model.PortMonitor.Entries.Add(new("TCP", AddressFamily.InterNetwork, "127.0.0.1", 8123,
                "203.0.113.10", 443, "Established", 4202, "Preview.Server"));
            Model.PortMonitor.Entries.Add(new("UDP", AddressFamily.InterNetworkV6, "::1", 5353,
                "::", null, "Bound", 1201, "Demo.Discovery"));
            Model.FileUnlocker.Entries.Add(new("C:\\Preview\\document.txt", 0x1234, 4202,
                "Preview.Editor", "C:\\Preview\\Editor.exe",
                new DateTimeOffset(2026, 10, 8, 8, 30, 0, TimeSpan.FromHours(8)), false));
            NetworkItem = new ApplicationTrafficItemViewModel(new("preview-network", "示例传输服务", null, false,
                32768, 131072, 2097152, 8388608,
                [new(new(4202, new DateTimeOffset(2026, 10, 8, 8, 30, 0, TimeSpan.FromHours(8))),
                    "Preview.Server", null, false, false, 32768, 131072, 2097152, 8388608)]), null);
            Model.NetworkTraffic.Items.Add(NetworkItem);
            var lottery = new Lazy<LotteryViewModel>(() => new(new LotteryStore(Path.Combine(_root, "lottery.json")),
                new OfflineLotteryClient(), _ => false,
                () => new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.FromHours(8)), _ => null));
            var field = typeof(MainViewModel).GetField("_lottery", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field!.SetValue(Model, lottery);
            Assert.NotEqual(IntPtr.Zero, new WindowInteropHelper(Window).EnsureHandle());
        }

        public MainWindow Window { get; }
        public Grid ContentRoot { get; }
        public MainViewModel Model { get; }
        public ApplicationTrafficItemViewModel NetworkItem { get; }

        public async Task<FrameworkElement> SelectPageAsync(string navigationName)
        {
            // DataContext 的绑定传播由 Dispatcher 调度；先等待命令与模板解析完成。
            await LayoutAsync(1280, 760);
            var navigation = Assert.IsAssignableFrom<ToggleButton>(Window.FindName(navigationName));
            Assert.NotNull(navigation.Command);
            Assert.True(navigation.Command!.CanExecute(null));
            // 网页入口的 Click 会启动生产浏览器；这里只执行所有六个入口共有的导航命令。
            navigation.Command.Execute(null);
            await LayoutAsync(1280, 760);
            var host = ContentRoot.Children.OfType<ContentControl>().Single();
            Assert.Same(Model.CurrentTool, host.Content);
            return Descendants<FrameworkElement>(host).First(x => ReferenceEquals(x.DataContext, Model.CurrentTool)
                && (x is UserControl || x is StackPanel));
        }

        public async Task LayoutAsync(int width, int height)
        {
            var content = ContentRoot;
            content.Measure(new Size(width, height));
            content.Arrange(new Rect(0, 0, width, height));
            content.UpdateLayout();
            foreach (var panel in Descendants<AttendancePanel>(content))
            {
                if (!_isolatedAttendancePanels.Add(panel)) continue;
                var field = typeof(AttendancePanel).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(field);
                field!.SetValue(panel, new AttendanceStore(Path.Combine(_root, "attendance")));
            }
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            content.UpdateLayout();
            Assert.False(Window.IsVisible);
            Assert.False(Window.IsLoaded);
            Assert.False(Model.NetworkTraffic.IsMonitoring);
            Assert.True(Model.PortMonitor.IsPaused);
        }

        public void ApplyAppearance(bool dark)
        {
            // 执行主窗口真实外观应用入口，保留主题标签；不调用写入用户偏好的 SaveDarkPreference。
            var field = typeof(MainWindow).GetField("_isDarkTheme", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field!.SetValue(Window, dark);
            var method = typeof(MainWindow).GetMethod("ApplyAppearance", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(Window, null);
        }

        public void ShowContentHost()
        {
            Assert.Null(_contentHost);
            Window.Content = null;
            _contentHost = new Window
            {
                Content = ContentRoot, Resources = Window.Resources, DataContext = Model,
                FontFamily = Window.FontFamily, FontSize = Window.FontSize,
                Width = 1280, Height = 760, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, ShowActivated = false, Opacity = 0
            };
            _contentHost.SetResourceReference(Control.BackgroundProperty, "ToolboxBackgroundBrush");
            _contentHost.SetResourceReference(Control.ForegroundProperty, "ToolboxTextBrush");
            _contentHost.Show();
            Assert.False(Window.IsLoaded);
        }

        public void Dispose()
        {
            _contentHost?.Close();
            Window.Close();
            Model.Dispose();
            _countdown.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    private sealed class EmptyCountdownStore : ICountdownStateStore
    {
        public CountdownState? Load() => null;
        public void Save(CountdownState state) { }
        public void Clear() { }
    }

    private sealed class OfflineLotteryClient : ILotteryClient
    {
        public Task<IReadOnlyList<LotteryDraw>> FetchAsync(bool allHistory, IProgress<string>? progress, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<LotteryDraw>>([]);
    }
}
