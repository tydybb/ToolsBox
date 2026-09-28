using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class WorkCountdownViewHolidayFunTests
{
    [Fact]
    public Task HolidayFunText_VisibleWithCopy_NearHoliday_CollapsedWithoutData() => WpfTestThread.RunAsync(async () =>
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
            var fun = Assert.IsType<TextBlock>(view.FindName("HolidayFunText"));
            Assert.Equal(vm.HolidayFunText, fun.Text);
            Assert.Equal(Visibility.Visible, fun.Visibility);

            using var quiet = new WorkCountdownViewModel(() => new(2026, 11, 15, 9, 30, 0), new EmptyStore(), false);
            view.DataContext = quiet;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("", fun.Text);
            Assert.Equal(Visibility.Collapsed, fun.Visibility);
        }
        finally { shell.Close(); }
    });

    private sealed class EmptyStore : ICountdownStateStore
    {
        public CountdownState? Load() => null;
        public void Save(CountdownState state) { }
        public void Clear() { }
    }
}
