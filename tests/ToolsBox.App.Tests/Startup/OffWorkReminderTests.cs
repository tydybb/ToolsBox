using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ToolsBox.App.WorkCountdown;

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

    private sealed class EmptyStore : ICountdownStateStore
    {
        public CountdownState? Load() => null;
        public void Save(CountdownState state) { }
        public void Clear() { }
    }
}
