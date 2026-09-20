using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class WorkCountdownViewTests
{
    [Theory]
    [InlineData("09:30")]
    [InlineData("09：30")]
    [InlineData("0930")]
    public Task View_BindsInputCommandsAndCountdown(string timeInput) => WpfTestThread.RunAsync(async () =>
    {
        Type? type = typeof(App).Assembly.GetType("ToolsBox.App.Views.WorkCountdownView");
        Assert.NotNull(type);
        var view = Assert.IsAssignableFrom<UserControl>(Activator.CreateInstance(type!));
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 9, 30, 0), new EmptyStore(), false);
        var shell = new MainWindow(vm, _ => { });
        try
        {
            view.Resources = shell.Resources;
            view.DataContext = vm;
            view.Measure(new Size(760, 900));
            view.Arrange(new Rect(0, 0, 760, 900));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var time = Assert.IsType<TextBox>(view.FindName("StartTimeInput"));
            var hours = Assert.IsType<TextBox>(view.FindName("OvertimeInput"));
            var start = Assert.IsType<Button>(view.FindName("StartCountdownButton"));
            var countdown = Assert.IsType<TextBlock>(view.FindName("CountdownText"));
            var overtimePanel = Assert.IsType<StackPanel>(view.FindName("OvertimePanel"));
            var overtimeDuration = Assert.IsType<TextBlock>(view.FindName("OvertimeDuration"));
            var overtimePeriod = Assert.IsType<TextBlock>(view.FindName("OvertimePeriod"));
            var overtimeMessage = Assert.IsType<TextBlock>(view.FindName("OvertimeMessage"));
            Assert.Equal(Visibility.Collapsed, overtimePanel.Visibility);
            time.Text = timeInput;
            hours.Text = "2";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(timeInput, vm.StartTimeText);
            Assert.Equal("2", vm.OvertimeText);
            Assert.Same(vm.StartCommand, start.Command);
            start.Command.Execute(null);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("09:30", time.Text);
            Assert.Equal("11:30:00", countdown.Text);
            Assert.Equal(Visibility.Visible, overtimePanel.Visibility);
            Assert.Equal("计划加班：2 小时（不含休息）", overtimeDuration.Text);
            Assert.Equal("加班时段：2026-09-18 18:30 — 2026-09-18 21:00", overtimePeriod.Text);
            Assert.Contains("加班小副本", overtimeMessage.Text);
            Assert.Equal(vm.DayMode, Assert.IsType<ComboBox>(view.FindName("DayModeInput")).SelectedValue);
            var screenshot = new System.Windows.Media.Imaging.RenderTargetBitmap(760, 900, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            screenshot.Render(view);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(screenshot));
            using var imageFile = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, "countdown-view.png"));
            encoder.Save(imageFile);
            hours.Text = "15";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Contains("加班小副本", overtimeMessage.Text);
            start.Command.Execute(null);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Contains("别吹牛逼", overtimeMessage.Text);
            hours.Text = "0";
            start.Command.Execute(null);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(Visibility.Collapsed, overtimePanel.Visibility);
            hours.Text = "2";
            start.Command.Execute(null);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(Visibility.Visible, overtimePanel.Visibility);
            vm.ResetCommand.Execute(null);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(Visibility.Collapsed, overtimePanel.Visibility);
        }
        finally { shell.Close(); }
    });

    private sealed class EmptyStore : ICountdownStateStore
    {
        public CountdownState? Load() => null;
        public void Save(CountdownState state) { }
        public void Clear() { }
    }

    [Fact]
    public Task DispatcherTimer_RefreshesUntilDisposed() => WpfTestThread.RunAsync(async () =>
    {
        DateTime now = new(2026, 9, 18, 9, 30, 0);
        using var vm = new WorkCountdownViewModel(() => now, new EmptyStore());
        vm.StartTimeText = "09:30";
        vm.OvertimeText = "2";
        vm.StartCommand.Execute(null);
        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.RemainingText) && vm.RemainingText == "11:00:00") updated.TrySetResult();
        };
        now = now.AddMinutes(30);
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.Dispose();
        now = now.AddMinutes(30);
        await Task.Delay(1200);
        Assert.Equal("11:00:00", vm.RemainingText);
    });
}
