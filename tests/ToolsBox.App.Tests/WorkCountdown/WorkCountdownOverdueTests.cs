using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ToolsBox.App.Views;
using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class WorkCountdownOverdueTests
{
    [Theory]
    [InlineData(0, 0, 0, "00:00:00")]
    [InlineData(0, 0, 999, "00:00:00")]
    [InlineData(0, 1, 0, "00:00:01")]
    [InlineData(25, 123, 0, "25:02:03")]
    public Task FinalEnd_CountsElapsedWholeSeconds_WithCumulativeHours(int hours, int seconds, int milliseconds, string expected) =>
        WpfTestThread.RunAsync(async () =>
        {
            DateTime now = new(2026, 9, 18, 9, 30, 0);
            using var vm = new WorkCountdownViewModel(() => now, new MemoryStore(), false);
            vm.StartTimeText = "09:30";
            vm.OvertimeText = "2";
            vm.StartCommand.Execute(null);
            var view = new WorkCountdownView { DataContext = vm };
            now = vm.Schedule!.End.AddHours(hours).AddSeconds(seconds).AddMilliseconds(milliseconds);
            vm.Refresh();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            Assert.Equal("你已无偿加班", vm.StatusText);
            Assert.Equal("00:00:00", vm.RemainingText);
            Assert.Equal(expected, Assert.IsType<TextBlock>(view.FindName("CountdownText")).Text);
        });

    [Fact]
    public Task Notice_StaysHiddenUntilFinalEnd_AndResetHidesIt() => WpfTestThread.RunAsync(async () =>
    {
        DateTime now = new(2026, 9, 18, 9, 30, 0);
        using var vm = new WorkCountdownViewModel(() => now, new MemoryStore(), false);
        var view = new WorkCountdownView { DataContext = vm };
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var notice = Assert.IsType<TextBlock>(view.FindName("OverdueNotice"));
        var clock = Assert.IsType<TextBlock>(view.FindName("CountdownText"));
        Assert.Equal(Visibility.Collapsed, notice.Visibility);
        Assert.Equal("--:--:--", clock.Text);

        vm.StartTimeText = "09:30";
        vm.OvertimeText = "2";
        vm.StartCommand.Execute(null);
        now = new(2026, 9, 18, 19, 15, 0);
        vm.Refresh();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(Visibility.Collapsed, notice.Visibility);
        Assert.Equal("01:45:00", clock.Text);
        Assert.Contains("加班小副本", vm.OvertimeMessage);

        now = vm.Schedule!.End.AddMilliseconds(-1);
        vm.Refresh();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(Visibility.Collapsed, notice.Visibility);
        Assert.Equal("00:00:01", clock.Text);
        now = vm.Schedule.End;
        vm.Refresh();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(Visibility.Visible, notice.Visibility);
        Assert.Equal("你已无偿加班", notice.Text);
        Assert.Equal("00:00:00", clock.Text);

        now = now.AddSeconds(7);
        vm.Refresh();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal("00:00:07", clock.Text);
        view.Measure(new Size(760, 1000));
        view.Arrange(new Rect(0, 0, 760, 1000));
        var screenshot = new System.Windows.Media.Imaging.RenderTargetBitmap(760, 1000, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        screenshot.Render(view);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(screenshot));
        using (var imageFile = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, "countdown-overdue-view.png")))
            encoder.Save(imageFile);
        vm.ResetCommand.Execute(null);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(Visibility.Collapsed, notice.Visibility);
        Assert.Equal("--:--:--", clock.Text);
    });

    [Fact]
    public Task DraftChanges_KeepActiveElapsedTime_AndRecalculationUsesNewEnd() => WpfTestThread.RunAsync(async () =>
    {
        DateTime now = new(2026, 9, 18, 18, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new MemoryStore(), false);
        vm.StartCommand.Execute(null);
        var view = new WorkCountdownView { DataContext = vm };
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var clock = Assert.IsType<TextBlock>(view.FindName("CountdownText"));
        Assert.Equal("00:30:00", clock.Text);
        Assert.Equal("你已无偿加班", vm.StatusText);

        vm.OvertimeText = "2";
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.True(vm.HasPendingChanges);
        Assert.Equal("00:30:00", clock.Text);
        vm.StartCommand.Execute(null);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal("距离预计下班", vm.StatusText);
        Assert.Equal("02:00:00", clock.Text);
    });

    [Fact]
    public Task RestoredOverdueTask_UsesOriginalEnd_AndClockCanMoveBackwards() => WpfTestThread.RunAsync(async () =>
    {
        DateTime now = new(2026, 9, 18, 22, 12, 3);
        var store = new MemoryStore { State = new(new(2026, 9, 18), "09:30", CountdownDayMode.Workday, 2) };
        using var vm = new WorkCountdownViewModel(() => now, store, false);
        var view = new WorkCountdownView { DataContext = vm };
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var clock = Assert.IsType<TextBlock>(view.FindName("CountdownText"));
        Assert.Equal("你已无偿加班", vm.StatusText);
        Assert.Equal("01:12:03", clock.Text);

        now = new(2026, 9, 18, 20, 59, 59);
        vm.Refresh();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal("距离预计下班", vm.StatusText);
        Assert.Equal("00:00:01", clock.Text);
    });

    private sealed class MemoryStore : ICountdownStateStore
    {
        public CountdownState? State { get; set; }
        public CountdownState? Load() => State;
        public void Save(CountdownState state) => State = state;
        public void Clear() => State = null;
    }
}
