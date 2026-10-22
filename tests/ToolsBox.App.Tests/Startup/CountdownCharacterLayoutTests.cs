using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ToolsBox.App.Views;
using ToolsBox.App.WorkCountdown;
using Xunit.Abstractions;

namespace ToolsBox.App.Tests.Startup;

public sealed class CountdownCharacterLayoutTests
{
    private readonly ITestOutputHelper _output;

    public CountdownCharacterLayoutTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OrdinaryCard_IsCompactAndFollowsLiveTheme(bool dark) => WpfTestThread.RunAsync(async () =>
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 11, 9, 10, 0, 0), new EmptyStore(), false)
        { DayMode = CountdownDayMode.Workday, StartTimeText = "08:30", OvertimeText = "0" };
        vm.StartCommand.Execute(null);
        var shell = new MainWindow(vm, _ => { });
        var view = new WorkCountdownView { Resources = shell.Resources, DataContext = vm };
        shell.Content = view;
        try
        {
            var apply = typeof(App).Assembly.GetType("ToolsBox.App.Infrastructure.ComfortAppearance")!
                .GetMethod("Apply", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
            foreach (bool theme in new[] { dark, !dark, dark })
            {
                apply.Invoke(null, [shell, theme]);
                view.Measure(new Size(760, 640));
                view.Arrange(new Rect(0, 0, 760, 640));
                view.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var card = Assert.IsType<Border>(view.FindName("CountdownCard"));
                Assert.Equal(((SolidColorBrush)shell.Resources["ToolboxSurfaceBrush"]).Color,
                    Assert.IsType<SolidColorBrush>(card.Background).Color);
                AssertSeparatedRegions(view, card);
                Assert.Equal(Visibility.Collapsed,
                    Assert.IsType<TextBlock>(view.FindName("CountdownStatusText")).Visibility);
                Assert.Equal(Visibility.Visible,
                    Assert.Single(Descendants<TextBlock>(card), text => text.Text == vm.AttendanceText).Visibility);
                Assert.Equal(vm.Card.Headline, Assert.IsType<TextBlock>(view.FindName("FlavorText")).Text);
                Assert.Equal(((SolidColorBrush)shell.Resources["ToolboxAccentBrush"]).Color,
                    Assert.IsType<SolidColorBrush>(((TextBlock)view.FindName("CountdownText")).Foreground).Color);
            }
        }
        finally { shell.Close(); }
    });

    [Fact]
    public Task BrandIcon_UsesHighResolutionBitmap() => WpfTestThread.RunAsync(() =>
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 10, 8, 9, 0, 0), new EmptyStore(), false);
        var shell = new MainWindow(vm, _ => { });
        try
        {
            var content = Assert.IsAssignableFrom<FrameworkElement>(shell.Content);
            content.Measure(new Size(1280, 760));
            content.Arrange(new Rect(0, 0, 1280, 760));
            var brand = Assert.Single(Descendants<Image>((DependencyObject)shell.Content));
            var source = Assert.IsAssignableFrom<BitmapSource>(brand.Source);
            Assert.True(source.PixelWidth >= 1024, $"品牌图实际像素宽度仅 {source.PixelWidth}，放大时不应读取 ICO 的小帧。");
            Assert.Equal(BitmapScalingMode.HighQuality, RenderOptions.GetBitmapScalingMode(brand));
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(760, false, 0)]
    [InlineData(1060, false, 0)]
    [InlineData(760, true, 0)]
    [InlineData(1060, true, 0)]
    [InlineData(760, false, 2)]
    [InlineData(1060, true, 2)]
    public Task CountdownCard_RendersSeparateRightCharacterArea(int width, bool dark, int overtime) => WpfTestThread.RunAsync(async () =>
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 10, 9, 16, 45, 0), new EmptyStore(), false);
        vm.StartTimeText = "08:00";
        vm.OvertimeText = overtime.ToString();
        vm.StartCommand.Execute(null);
        var shell = new MainWindow(vm, _ => { });
        var view = new WorkCountdownView { Resources = shell.Resources, DataContext = vm };
        shell.Content = view;
        try
        {
            typeof(App).Assembly.GetType("ToolsBox.App.Infrastructure.ComfortAppearance")!
                .GetMethod("Apply", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
                .Invoke(null, [shell, dark]);
            view.Measure(new Size(width, 640));
            view.Arrange(new Rect(0, 0, width, 640));
            view.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var companion = Assert.IsType<CountdownCompanion>(view.FindName("CountdownCharacter"));
            var character = companion.Sticker;
            Assert.IsAssignableFrom<BitmapSource>(character.Source);
            var name = companion.NameText;
            var caption = Assert.IsType<TextBlock>(view.FindName("CountdownCharacterCaption"));
            Assert.NotEmpty(name.Text);
            Assert.NotEmpty(caption.Text);
            Assert.Equal(80, character.ActualWidth);
            var countdown = Assert.IsType<TextBlock>(view.FindName("CountdownText"));
            Assert.True(countdown.ActualWidth >= 300, "时间显示空间过窄");
            var card = Assert.IsType<Border>(view.FindName("CountdownCard"));
            Assert.Equal(((SolidColorBrush)shell.Resources["ToolboxSurfaceBrush"]).Color, Assert.IsType<SolidColorBrush>(card.Background).Color);
            Assert.Same(card, ClosestBorder(character));
            Assert.Equal(dark, companion.IsDarkTheme);
            if (dark) Assert.NotNull(character.Effect);
            else Assert.Null(character.Effect);
            AssertSeparatedRegions(view, card);
            Assert.True(companion.ActualWidth - companion.MotionGroup.ActualWidth >= 140,
                "右侧人物区须保留真实的水平活动空间。");
            _output.WriteLine($"width={width}; dark={dark}; overtime={overtime}; card={card.ActualWidth:0.##}x{card.ActualHeight:0.##}; area={companion.ActualWidth:0.##}x{companion.ActualHeight:0.##}");
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<TextBlock>(view.FindName("CountdownStatusText")).Visibility);
            Assert.Equal(vm.Card.Headline, Assert.IsType<TextBlock>(view.FindName("FlavorText")).Text);
            Assert.Null(view.FindName("OvertimePeriod"));
            Assert.Null(view.FindName("OvertimeMessage"));
            Assert.DoesNotContain(Descendants<TextBlock>(card), text =>
                text.Text == vm.ActiveTaskText || text.Text == "下班搭子");
            var duration = Assert.IsType<TextBlock>(view.FindName("OvertimeDuration"));
            Assert.Equal(overtime > 0 ? Visibility.Visible : Visibility.Collapsed,
                Assert.IsType<StackPanel>(view.FindName("OvertimePanel")).Visibility);
            if (overtime > 0) Assert.Contains($"{overtime} 小时", duration.Text);
            Assert.True(companion.AnimationEnabled);
            Assert.Null(companion.ToolTip);
            Assert.Null(character.ToolTip);
            Assert.Null(name.ToolTip);
            Assert.DoesNotContain(Descendants<TextBlock>(card), text => text.Text.Contains("本地逐帧"));
            Assert.Empty(Descendants<CheckBox>(card));
            Assert.DoesNotContain(Descendants<TextBlock>(card), text => text.Text.Contains("非官方补绘"));
            var settings = Assert.IsType<Border>(view.FindName("CountdownSettingsCard"));
            Assert.True(card.TranslatePoint(new Point(), view).Y < settings.TranslatePoint(new Point(), view).Y);
            Assert.InRange(card.TranslatePoint(new Point(), view).Y, 0, 120);
            WpfTestSnapshot.SaveWindowContent(shell, width, 640, $"countdown-characters-{width}-{(dark ? "dark" : "light")}-ot{overtime}.png");
        }
        finally { shell.Close(); }
    });

    [Theory]
    [InlineData(640, false)]
    [InlineData(640, true)]
    [InlineData(1060, false)]
    [InlineData(1060, true)]
    public Task LongCopy_StaysWithinLeftColumnAtSmallAndLargeSizes(int width, bool dark) => WpfTestThread.RunAsync(async () =>
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 10, 8, 10, 0, 0), new EmptyStore(), false)
        { DayMode = CountdownDayMode.Workday, StartTimeText = "08:30", OvertimeText = "2" };
        vm.StartCommand.Execute(null);
        var shell = new MainWindow(vm, _ => { });
        var view = new WorkCountdownView { Resources = shell.Resources, DataContext = vm };
        shell.Content = view;
        try
        {
            typeof(App).Assembly.GetType("ToolsBox.App.Infrastructure.ComfortAppearance")!
                .GetMethod("Apply", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
                .Invoke(null, [shell, dark]);
            var flavor = Assert.IsType<TextBlock>(view.FindName("FlavorText"));
            var caption = Assert.IsType<TextBlock>(view.FindName("CountdownCharacterCaption"));
            flavor.Text = string.Concat(Enumerable.Repeat(vm.FlavorText, 4));
            caption.Text = string.Concat(Enumerable.Repeat(vm.Reaction.Caption, 4));
            view.Measure(new Size(width, 640));
            view.Arrange(new Rect(0, 0, width, 640));
            view.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var card = Assert.IsType<Border>(view.FindName("CountdownCard"));
            AssertSeparatedRegions(view, card);
            Assert.True(flavor.ActualHeight > 22 && caption.ActualHeight > 20,
                "Long-copy fixture must actually wrap onto multiple rows.");
            Assert.False(card.ClipToBounds);
            Assert.True(double.IsNaN(card.Height), "卡片须自然扩展，不能固定高度截断长文案。");
            _output.WriteLine($"long-copy width={width}; dark={dark}; card={card.ActualWidth:0.##}x{card.ActualHeight:0.##}; flavor-height={flavor.ActualHeight:0.##}; caption-height={caption.ActualHeight:0.##}");
            WpfTestSnapshot.SaveWindowContent(shell, width, 640, $"countdown-columns-long-{width}-{(dark ? "dark" : "light")}.png");
        }
        finally { shell.Close(); }
    });

    [Fact]
    public Task CompactCard_PreservesLateAndUnpaidOvertimeNotices() => WpfTestThread.RunAsync(async () =>
    {
        DateTime now = new(2026, 11, 9, 10, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new EmptyStore(), false)
        { DayMode = CountdownDayMode.Workday, StartTimeText = "10:00", OvertimeText = "0" };
        vm.StartCommand.Execute(null);
        var shell = new MainWindow(vm, _ => { });
        var view = new WorkCountdownView { Resources = shell.Resources, DataContext = vm };
        shell.Content = view;
        try
        {
            view.Measure(new Size(760, 640));
            view.Arrange(new Rect(0, 0, 760, 640));
            view.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var card = Assert.IsType<Border>(view.FindName("CountdownCard"));
            var late = Assert.Single(Descendants<TextBlock>(card), text => text.Text == vm.AttendanceText);
            Assert.Equal("今日迟到", late.Text);
            Assert.Equal(Visibility.Visible, late.Visibility);
            var overdue = Assert.IsType<TextBlock>(view.FindName("OverdueNotice"));
            Assert.Equal(Visibility.Collapsed, overdue.Visibility);
            now = new(2026, 11, 9, 18, 45, 0);
            vm.Refresh();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(Visibility.Visible, overdue.Visibility);
            Assert.Equal("你已无偿加班", overdue.Text);
            Assert.Equal("00:15:00", Assert.IsType<TextBlock>(view.FindName("CountdownText")).Text);
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<TextBlock>(view.FindName("CountdownStatusText")).Visibility);
        }
        finally { shell.Close(); }
    });

    [Theory]
    [InlineData(9, "lipu/calm", "厘普")]
    [InlineData(10, "duodong/calm", "多栋")]
    [InlineData(11, "xiuzhen/calm", "秀珍")]
    [InlineData(12, "tianzhong/calm", "田中")]
    [InlineData(13, "xiaohe/calm", "小河")]
    public Task CharacterName_FollowsTheDisplayedCharacter(int day, string key, string expectedName) => WpfTestThread.RunAsync(async () =>
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 11, day, 9, 0, 0), new EmptyStore(), false)
        { DayMode = CountdownDayMode.Workday, StartTimeText = "08:30", OvertimeText = "0" };
        vm.StartCommand.Execute(null);
        var shell = new MainWindow(vm, _ => { });
        var view = new WorkCountdownView { Resources = shell.Resources, DataContext = vm };
        shell.Content = view;
        try
        {
            view.Measure(new Size(760, 640));
            view.Arrange(new Rect(0, 0, 760, 640));
            view.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var companion = Assert.IsType<CountdownCompanion>(view.FindName("CountdownCharacter"));
            Assert.Equal(key, companion.AnimationKey);
            Assert.Equal(expectedName, companion.NameText.Text);
        }
        finally { shell.Close(); }
    });

    private static Border? ClosestBorder(DependencyObject child)
    {
        DependencyObject? current = VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is Border border) return border;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static void AssertSeparatedRegions(WorkCountdownView view, Border card)
    {
        var copy = Assert.IsType<StackPanel>(view.FindName("CountdownCopyPanel"));
        var area = Assert.IsType<Grid>(view.FindName("CountdownCharacterArea"));
        var companion = Assert.IsType<CountdownCompanion>(view.FindName("CountdownCharacter"));
        var copyBounds = new Rect(copy.TranslatePoint(new Point(), card), copy.RenderSize);
        var areaBounds = new Rect(area.TranslatePoint(new Point(), card), area.RenderSize);
        Assert.True(copy.ActualWidth >= 300 && area.ActualWidth >= 220);
        Assert.True(area.ActualHeight >= 160, "右侧人物区须具备垂直活动空间。");
        Assert.Equal(copyBounds.Top, areaBounds.Top, 1);
        Assert.Equal(20, areaBounds.Left - copyBounds.Right, 1);
        Assert.True(areaBounds.Left >= copyBounds.Right + 20 - 0.1);
        Assert.Equal(area.ActualWidth, companion.ActualWidth, 1);
        Assert.Equal(area.ActualHeight, companion.ActualHeight, 1);
        Assert.True(area.ClipToBounds);
        Assert.Equal(card.ActualWidth - 11, areaBounds.Right, 1);
        Assert.Equal(Math.Max(copy.ActualHeight, area.ActualHeight) + 22, card.ActualHeight, 1);
        foreach (TextBlock text in Descendants<TextBlock>(copy).Where(text => text.Visibility == Visibility.Visible))
        {
            var bounds = new Rect(text.TranslatePoint(new Point(), card), text.RenderSize);
            Assert.Equal(copyBounds.Left, bounds.Left, 1);
            Assert.True(bounds.Right <= copyBounds.Right + 0.1 && bounds.Bottom <= card.ActualHeight - 11 + 0.1,
                $"{text.Name} must stay wholly inside the left column: text={bounds}; left={copyBounds}.");
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (T nested in Descendants<T>(child)) yield return nested;
        }
    }

    private sealed class EmptyStore : ICountdownStateStore
    {
        public CountdownState? Load() => null;
        public void Save(CountdownState state) { }
        public void Clear() { }
    }
}
