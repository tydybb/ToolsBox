using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class WorkCountdownViewFlavorTests
{
    [Fact]
    public Task FlavorText_ShowsWeekdayCopyOnOrdinaryDay_AndHolidayCopyWhenNearHoliday() => WpfTestThread.RunAsync(async () =>
    {
        Type? type = typeof(App).Assembly.GetType("ToolsBox.App.Views.WorkCountdownView");
        Assert.NotNull(type);
        var view = Assert.IsAssignableFrom<UserControl>(Activator.CreateInstance(type!));
        using var ordinary = new WorkCountdownViewModel(() => new(2026, 11, 13, 9, 30, 0), new EmptyStore(), false);
        var shell = new MainWindow(ordinary, _ => { });
        try
        {
            view.Resources = shell.Resources;
            view.DataContext = ordinary;
            view.Measure(new Size(760, 900));
            view.Arrange(new Rect(0, 0, 760, 900));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var flavor = Assert.IsType<TextBlock>(view.FindName("FlavorText"));
            Assert.Equal(ordinary.FlavorText, flavor.Text);
            Assert.Equal(Visibility.Visible, flavor.Visibility);
            Assert.Contains("周五", flavor.Text);

            using var nearHoliday = new WorkCountdownViewModel(() => new(2026, 9, 18, 9, 30, 0), new EmptyStore(), false);
            view.DataContext = nearHoliday;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Contains("距离中秋节假期还有 7 天", flavor.Text);
            Assert.Equal(Visibility.Visible, flavor.Visibility);
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
