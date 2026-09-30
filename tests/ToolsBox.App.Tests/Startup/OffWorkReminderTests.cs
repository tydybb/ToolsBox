using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ToolsBox.App.Views;
using ToolsBox.App.WorkCountdown;
using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.App.Tests.Startup;

public sealed class OffWorkReminderTests
{
    [Fact]
    public Task Reminder_IsDeliveredOnce_WhileAnotherToolIsSelected() => WpfTestThread.RunAsync(() =>
    {
        DateTime now = new(2026, 9, 18, 9, 30, 0);
        using var countdown = new WorkCountdownViewModel(() => now, new EmptyStore(), false);
        countdown.StartTimeText = "0930";
        countdown.OvertimeText = "2";
        countdown.StartCommand.Execute(null);
        int notifications = 0;
        Action<WorkCountdownViewModel> notify = vm =>
        {
            Assert.Same(countdown, vm);
            Assert.Equal(new DateTime(2026, 9, 18, 21, 0, 0), vm.Schedule!.End);
            notifications++;
        };
        var constructor = typeof(MainWindow).GetConstructor([typeof(WorkCountdownViewModel), typeof(Action<WorkCountdownViewModel>)]);
        Assert.NotNull(constructor);
        var window = (MainWindow)constructor!.Invoke([countdown, notify]);
        try
        {
            var vm = Assert.IsType<MainViewModel>(window.DataContext);
            vm.ShowFileUnlockerCommand.Execute(null);
            Assert.Same(vm.FileUnlocker, vm.CurrentTool);
            countdown.Refresh();
            Assert.Equal(0, notifications);
            now = new(2026, 9, 18, 21, 0, 0);
            countdown.Refresh();
            countdown.Refresh();
            now = now.AddMinutes(5);
            countdown.Refresh();
            Assert.Equal(1, notifications);
        }
        finally { window.Close(); }
        countdown.Refresh();
        Assert.Equal(1, notifications);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReminderWindow_IsNonActivating_AndDisplaysElapsedTime() => WpfTestThread.RunAsync(async () =>
    {
        DateTime now = new(2026, 9, 18, 18, 35, 0);
        using var vm = new WorkCountdownViewModel(() => now, new EmptyStore(), false);
        vm.StartTimeText = "0930";
        vm.StartCommand.Execute(null);
        Type? type = typeof(App).Assembly.GetType("ToolsBox.App.Views.OffWorkReminderWindow");
        Assert.NotNull(type);
        var window = Assert.IsAssignableFrom<Window>(Activator.CreateInstance(type!, vm));
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        try
        {
            window.Measure(new Size(520, 320));
            window.Arrange(new Rect(0, 0, 520, 320));
            window.UpdateLayout();
            var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
            content.Measure(new Size(520, 320));
            content.Arrange(new Rect(0, 0, 520, 320));
            content.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("即将关机", window.Title);
            var background = Assert.IsType<SolidColorBrush>(window.Background);
            Assert.Equal(Color.FromRgb(0, 120, 215), background.Color);
            Assert.True(window.Topmost);
            Assert.False(window.ShowActivated);
            Assert.Null(window.FindName("ReminderDisclaimer"));
            Assert.Equal("即将关机", Assert.IsType<TextBlock>(window.FindName("WarningTitle")).Text);
            Assert.Equal("你已经下班，我要把你电脑关了", Assert.IsType<TextBlock>(window.FindName("ReminderMessage")).Text);
            // 提醒窗是收工时刻：节日文案要切到收工语气，距离天数从明天起算（距中秋 7 天 → 显示 6 天）。
            Assert.Contains("收工！距离中秋节假期还有 6 天", Assert.IsType<TextBlock>(window.FindName("FlavorText")).Text);
            Assert.Equal("你已无偿加班", Assert.IsType<TextBlock>(window.FindName("OverdueNotice")).Text);
            var timer = Assert.IsType<TextBlock>(window.FindName("OverdueTimer"));
            Assert.Equal("00:05:00", timer.Text);
            var dismissButton = Assert.IsType<Button>(window.FindName("DismissButton"));
            Assert.Equal("关闭", dismissButton.Content);
            Assert.Equal(Visibility.Visible, dismissButton.Visibility);
            Assert.True(dismissButton.IsEnabled);
            Assert.Equal(110, dismissButton.ActualWidth);
            Assert.Equal(36, dismissButton.ActualHeight);
            Assert.InRange(dismissButton.TransformToAncestor(content).Transform(new Point(0, 0)).Y + dismissButton.ActualHeight, 0, 320);
            now = now.AddMinutes(1);
            vm.Refresh();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("00:06:00", timer.Text);
            WpfTestSnapshot.SaveWindowContent(window, 520, 320, "off-work-reminder.png");
            dismissButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(closed);
            now = now.AddMinutes(1);
            vm.Refresh();
            Assert.Equal("00:07:00", vm.ElapsedText);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task FinishButton_MatchesCountdownButton_AndFinishingClosesReminder() => WpfTestThread.RunAsync(async () =>
    {
        DateTime now = new(2026, 9, 18, 21, 5, 0);
        using var vm = new WorkCountdownViewModel(() => now, new EmptyStore(), false);
        vm.StartTimeText = "0930";
        vm.OvertimeText = "2";
        vm.StartCommand.Execute(null);
        Assert.True(vm.IsOverdue);
        var window = new OffWorkReminderWindow(vm);
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        try
        {
            window.Measure(new Size(520, 320));
            window.Arrange(new Rect(0, 0, 520, 320));
            window.UpdateLayout();
            var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
            content.Measure(new Size(520, 320));
            content.Arrange(new Rect(0, 0, 520, 320));
            content.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            // 与倒计时卡片上的“下班”按钮完全一致：同名、同命令、同文案。
            var button = Assert.IsType<Button>(window.FindName("FinishWorkButton"));
            Assert.Equal("下班", button.Content);
            Assert.Same(vm.FinishCommand, button.Command);
            Assert.True(button.IsEnabled);

            button.Command.Execute(null);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            Assert.True(vm.IsFinished);
            Assert.Equal(now, vm.FinishedAt);
            Assert.True(closed);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task FinishButton_IsDisabledBeforeCountdownStarts() => WpfTestThread.RunAsync(async () =>
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 9, 30, 0), new EmptyStore(), false);
        var window = new OffWorkReminderWindow(vm);
        try
        {
            window.Measure(new Size(520, 320));
            window.Arrange(new Rect(0, 0, 520, 320));
            var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
            content.Measure(new Size(520, 320));
            content.Arrange(new Rect(0, 0, 520, 320));
            content.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            var button = Assert.IsType<Button>(window.FindName("FinishWorkButton"));
            Assert.False(button.IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ReminderWindow_Celebration_FiresOnlyOnHolidayDays_AndClearsOnClose() => WpfTestThread.RunAsync(() =>
    {
        // 2026-09-30 是国庆节前最后一个工作日，下班 18:35 已过 18:30。
        DateTime now = new(2026, 9, 30, 18, 35, 0);
        using var vm = new WorkCountdownViewModel(() => now, new EmptyStore(), false);
        vm.StartTimeText = "0930";
        vm.StartCommand.Execute(null);
        Assert.True(vm.IsOverdue);
        // 国庆 7 天 → 完整版（纸屑 + 烟花）
        Assert.Equal(CelebrationLevel.Full, vm.CelebrationLevel);

        var window = new OffWorkReminderWindow(vm);
        var canvas = Assert.IsAssignableFrom<Canvas>(window.FindName("CelebrationCanvas"));
        window.Measure(new Size(520, 320));
        window.Arrange(new Rect(0, 0, 520, 320));
        window.UpdateLayout();
        var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        content.Measure(new Size(520, 320));
        content.Arrange(new Rect(0, 0, 520, 320));
        content.UpdateLayout();
        try
        {
            // 覆盖层靠负边距越出根 Grid 铺满整扇窗：既向左上偏移，尺寸也比内容区更大。
            // 没有这条就只能证明“生成了元素”，证明不了纸屑真的落到看得见的地方。
            var origin = canvas.TransformToAncestor(content).Transform(new Point(0, 0));
            Assert.True(origin.X < 0 && origin.Y < 0, $"覆盖层原点 {origin} 未越出内容区");
            Assert.True(canvas.ActualWidth > content.ActualWidth && canvas.ActualHeight > content.ActualHeight,
                $"覆盖层 {canvas.ActualWidth}x{canvas.ActualHeight} 未大于内容区 {content.ActualWidth}x{content.ActualHeight}");

            Assert.False(canvas.IsHitTestVisible);   // 覆盖层不能挡住“下班 / 关闭”按钮
            Assert.Empty(canvas.Children);

            window.StartCelebration(CelebrationLevel.None);
            Assert.Empty(canvas.Children);

            window.StartCelebration(vm.CelebrationLevel);
            Assert.NotEmpty(canvas.Children);
        }
        finally { window.Close(); }
        Assert.Empty(canvas.Children);               // 关窗即停定时器并清空画布，不留残留
        return Task.CompletedTask;
    });

    [Fact]
    public void CelebrationLevel_FollowsHolidayLengthAndSkipsOrdinaryDays()
    {
        using var shortEve = new WorkCountdownViewModel(() => new DateTime(2026, 9, 24, 18, 35, 0), new EmptyStore(), false);
        Assert.Equal(CelebrationLevel.Light, shortEve.CelebrationLevel);  // 中秋 3 天 → 轻量版

        using var ordinary = new WorkCountdownViewModel(() => new DateTime(2026, 11, 11, 18, 35, 0), new EmptyStore(), false);
        Assert.Equal(CelebrationLevel.None, ordinary.CelebrationLevel);   // 普通日子不放
    }

    private sealed class EmptyStore : ICountdownStateStore
    {
        public CountdownState? Load() => null;
        public void Save(CountdownState state) { }
        public void Clear() { }
    }
}
