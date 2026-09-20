using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ToolsBox.App.Views;
using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App.Tests.Startup;

public sealed class FinishWorkIntegrationTests
{
    [Theory]
    [InlineData(18, 0, true, true, "03:00:00", "early")]
    [InlineData(18, 30, false, true, "02:30:00", "normal")]
    [InlineData(21, 15, false, false, "00:15:00", "overdue")]
    public Task FinishButton_DisplaysResultAndFreezesClock(
        int hour, int minute, bool early, bool incomplete, string frozen, string snapshot) => WpfTestThread.RunAsync(async () =>
    {
        DateTime now = new(2026, 9, 18, 9, 30, 0);
        using var vm = new WorkCountdownViewModel(() => now, new EmptyStore(), false);
        var notices = new List<string>();
        int deadlineNotices = 0;
        var window = CreateWindow(vm, _ => deadlineNotices++, result =>
            notices.Add(Read<string>(result, "FinishMessage") + "\n" + Read<string>(result, "FinishDetails")));
        try
        {
            var view = new WorkCountdownView { Resources = window.Resources, DataContext = vm };
            view.Measure(new Size(760, 1100));
            view.Arrange(new Rect(0, 0, 760, 1100));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var button = Assert.IsType<Button>(view.FindName("FinishWorkButton"));
            Assert.Equal("下班", button.Content);
            Assert.Same(Read<ICommand>(vm, "FinishCommand"), button.Command);
            Assert.False(button.IsEnabled);

            vm.StartTimeText = "0930";
            vm.OvertimeText = "2";
            vm.StartCommand.Execute(null);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(button.IsEnabled);
            now = new(2026, 9, 18, hour, minute, 0);
            // A click must use the fresh clock, without a preceding tick that could raise a deadline popup.
            button.Command.Execute(null);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(Read<bool>(vm, "IsFinished"));
            Assert.Equal(early, Read<bool>(vm, "IsEarlyDeparture"));
            Assert.False(button.IsEnabled);
            Assert.Equal(Color.FromRgb(99, 115, 129), Assert.IsType<SolidColorBrush>(button.Foreground).Color);
            if (early) Assert.Contains("早退", vm.AttendanceText);
            Assert.Single(notices);
            Assert.Equal(0, deadlineNotices);
            var status = Assert.IsType<TextBlock>(view.FindName("CountdownStatusText"));
            var details = Assert.IsType<TextBlock>(view.FindName("FinishDetailsText"));
            Assert.Equal(Read<string>(vm, "FinishMessage"), status.Text);
            Assert.Equal(Read<string>(vm, "FinishDetails"), details.Text);
            Assert.Equal(Visibility.Visible, status.Visibility);
            Assert.Equal(Visibility.Visible, details.Visibility);
            Assert.Contains(early ? "你早退了" : "抓紧回家吧", status.Text);
            Assert.Contains(now.ToString("yyyy-MM-dd HH:mm:ss"), details.Text);
            Assert.Equal(incomplete, details.Text.Contains("计划加班未完成"));
            Assert.Contains("计时已停止", Assert.IsType<TextBlock>(view.FindName("FinishedTimerCaption")).Text);
            Assert.Equal(Visibility.Collapsed, Assert.IsType<TextBlock>(view.FindName("OverdueNotice")).Visibility);
            Assert.Equal(Visibility.Collapsed, Assert.IsType<TextBlock>(view.FindName("OvertimeMessage")).Visibility);
            var clock = Assert.IsType<TextBlock>(view.FindName("CountdownText"));
            Assert.Equal(frozen, clock.Text);
            var snapshotWindow = new Window { Content = view, Background = view.Background };
            try { WpfTestSnapshot.SaveWindowContent(snapshotWindow, 760, 1100, $"countdown-finished-{snapshot}.png"); }
            finally { snapshotWindow.Content = null; snapshotWindow.Close(); }
            button.Command.Execute(null);
            now = now.AddDays(1);
            vm.Refresh();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(frozen, clock.Text);
            Assert.Single(notices);
            Assert.Equal(0, deadlineNotices);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Finish_ClosesExistingDeadlineWarning_AndWindowUnsubscribes() => WpfTestThread.RunAsync(() =>
    {
        DateTime now = new(2026, 9, 18, 9, 30, 0);
        using var vm = new WorkCountdownViewModel(() => now, new EmptyStore(), false);
        int results = 0;
        var window = CreateWindow(vm, _ => { }, _ => results++);
        var warning = new OffWorkReminderWindow(vm);
        bool warningClosed = false;
        warning.Closed += (_, _) => warningClosed = true;
        try
        {
            vm.StartTimeText = "0930";
            vm.OvertimeText = "2";
            vm.StartCommand.Execute(null);
            now = new(2026, 9, 18, 21, 5, 0);
            vm.Refresh();
            Assert.True(vm.IsOverdue);
            var reminderField = typeof(MainWindow).GetField("_offWorkReminder", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(reminderField);
            reminderField.SetValue(window, warning);
            Read<ICommand>(vm, "FinishCommand").Execute(null);
            Assert.True(warningClosed);
            Assert.False(vm.IsOverdue);
            Assert.Equal(1, results);

            var eventField = typeof(WorkCountdownViewModel).GetField("WorkFinished", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(eventField);
            Assert.Contains(((Delegate?)eventField.GetValue(vm))!.GetInvocationList(), handler => ReferenceEquals(handler.Target, window));
            window.Close();
            Assert.DoesNotContain(((Delegate?)eventField.GetValue(vm))?.GetInvocationList() ?? [], handler => ReferenceEquals(handler.Target, window));
        }
        finally { warning.Close(); window.Close(); }
        return Task.CompletedTask;
    });

    private static MainWindow CreateWindow(WorkCountdownViewModel vm,
        Action<WorkCountdownViewModel> deadline, Action<WorkCountdownViewModel> finished)
    {
        var constructor = typeof(MainWindow).GetConstructor([
            typeof(WorkCountdownViewModel), typeof(Action<WorkCountdownViewModel>), typeof(Action<WorkCountdownViewModel>)]);
        Assert.NotNull(constructor);
        return (MainWindow)constructor.Invoke([vm, deadline, finished]);
    }

    private static T Read<T>(WorkCountdownViewModel vm, string name)
    {
        var property = typeof(WorkCountdownViewModel).GetProperty(name);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<T>(property.GetValue(vm));
    }

    private sealed class EmptyStore : ICountdownStateStore
    {
        public CountdownState? Load() => null;
        public void Save(CountdownState state) { }
        public void Clear() { }
    }
}
