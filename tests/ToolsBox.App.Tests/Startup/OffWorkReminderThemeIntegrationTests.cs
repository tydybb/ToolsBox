using System.Reflection;
using System.Windows.Media;
using ToolsBox.App.Views;
using ToolsBox.App.WorkCountdown;
using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.App.Tests.Startup;

public sealed class OffWorkReminderThemeIntegrationTests
{
    [Fact]
    public Task ExistingReminder_FollowsMainWindowThemeWithoutReplacingArtwork() => WpfTestThread.RunAsync(() =>
    {
        using var countdown = new WorkCountdownViewModel(() => new(2026, 11, 10, 18, 35, 0), new EmptyStore(), false);
        var main = new MainWindow(countdown, _ => { }, _ => { });
        var reminder = new OffWorkReminderWindow(countdown);
        try
        {
            typeof(MainWindow).GetField("_offWorkReminder", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, reminder);
            object? originalArtwork = reminder.GetType().GetProperty("SelectedArtworkKey")?.GetValue(reminder);
            foreach (bool dark in new[] { false, true, false })
            {
                typeof(MainWindow).GetField("_isDarkTheme", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, dark);
                typeof(MainWindow).GetMethod("ApplyAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
                Assert.Equal(dark, reminder.Resources["ToolboxIsDarkTheme"]);
                Assert.Equal(Assert.IsType<SolidColorBrush>(main.Background).Color,
                    Assert.IsType<SolidColorBrush>(reminder.Background).Color);
                Assert.Equal(originalArtwork, reminder.GetType().GetProperty("SelectedArtworkKey")?.GetValue(reminder));
                Assert.Null(reminder.Owner);
                Assert.False(reminder.ShowActivated);
            }
        }
        finally
        {
            reminder.Close();
            main.Close();
        }
        return Task.CompletedTask;
    });

    private sealed class EmptyStore : ICountdownStateStore
    {
        public CountdownState? Load() => null;
        public void Save(CountdownState state) { }
        public void Clear() { }
    }
}
