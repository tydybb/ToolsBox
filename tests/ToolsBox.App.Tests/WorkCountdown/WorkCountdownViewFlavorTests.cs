using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ToolsBox.App.Views;
using ToolsBox.App.WorkCountdown;
using Xunit.Abstractions;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class WorkCountdownViewFlavorTests
{
    private readonly ITestOutputHelper _output;

    public WorkCountdownViewFlavorTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public Task FlavorAndCharacterCaption_FollowTheirOwnCopy() => WpfTestThread.RunAsync(async () =>
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
            Assert.Equal(ordinary.Card.Headline, flavor.Text);
            Assert.Equal(Visibility.Visible, flavor.Visibility);
            var facts = Assert.IsType<TextBlock>(view.FindName("CountdownDayFacts"));
            Assert.Equal(ordinary.Card.FactText, facts.Text);
            Assert.Equal(Visibility.Visible, facts.Visibility);
            Assert.Contains("周五", facts.Text);
            var caption = Assert.IsType<TextBlock>(view.FindName("CountdownCharacterCaption"));
            Assert.Equal(ordinary.Card.Caption, caption.Text);
            Assert.Equal(Visibility.Visible, caption.Visibility);
            Assert.Contains("周五", ordinary.Card.FactText);

            using var nearHoliday = new WorkCountdownViewModel(() => new(2026, 9, 18, 9, 30, 0), new EmptyStore(), false);
            view.DataContext = nearHoliday;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(nearHoliday.Card.Caption, caption.Text);
            Assert.Equal(Visibility.Visible, caption.Visibility);
            Assert.Equal(nearHoliday.Card.Headline, flavor.Text);
            Assert.Equal(Visibility.Visible, flavor.Visibility);
            Assert.Contains("距离中秋节假期还有 7 天", nearHoliday.Card.FactText);
        }
        finally { shell.Close(); }
    });

    [Theory]
    [InlineData("ordinary", false)]
    [InlineData("ordinary", true)]
    [InlineData("holiday", false)]
    [InlineData("holiday", true)]
    [InlineData("overtime", false)]
    [InlineData("overtime", true)]
    [InlineData("finished", false)]
    [InlineData("finished", true)]
    [InlineData("late", false)]
    [InlineData("early", true)]
    public Task CompactCard_PreservesVisibleCopyInEachTaskState(string state, bool dark) => WpfTestThread.RunAsync(async () =>
    {
        DateTime now = state switch
        {
            "holiday" => new(2026, 10, 8, 10, 0, 0),
            "finished" => new(2026, 10, 8, 18, 0, 0),
            "early" => new(2026, 11, 9, 16, 0, 0),
            _ => new(2026, 11, 9, 10, 0, 0)
        };
        using var vm = new WorkCountdownViewModel(() => now, new EmptyStore(), false)
        {
            DayMode = CountdownDayMode.Workday,
            StartTimeText = state == "late" ? "10:00" : "08:30",
            OvertimeText = state == "overtime" ? "2" : "0"
        };
        vm.StartCommand.Execute(null);
        if (state is "finished" or "early") vm.FinishCommand.Execute(null);
        var shell = new MainWindow(vm, _ => { });
        var view = new WorkCountdownView { Resources = shell.Resources, DataContext = vm };
        shell.Content = view;
        try
        {
            typeof(App).Assembly.GetType("ToolsBox.App.Infrastructure.ComfortAppearance")!
                .GetMethod("Apply", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
                .Invoke(null, [shell, dark]);
            view.Measure(new Size(760, 640));
            view.Arrange(new Rect(0, 0, 760, 640));
            view.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            var card = Assert.IsType<Border>(view.FindName("CountdownCard"));
            var flavor = Assert.IsType<TextBlock>(view.FindName("FlavorText"));
            var timer = Assert.IsType<TextBlock>(view.FindName("CountdownText"));
            var attendance = Assert.IsType<TextBlock>(view.FindName("AttendanceText"));
            var caption = Assert.IsType<TextBlock>(view.FindName("CountdownCharacterCaption"));
            Assert.Equal(vm.Card.Headline, flavor.Text);
            Assert.Equal(vm.TimerText, timer.Text);
            Assert.Equal(vm.AttendanceText, attendance.Text);
            Assert.Equal(vm.Card.Caption, caption.Text);
            Assert.All(new[] { flavor, timer, attendance, caption }, text =>
            {
                Assert.Equal(Visibility.Visible, text.Visibility);
                var bounds = new Rect(text.TranslatePoint(new Point(), card), text.RenderSize);
                Assert.True(bounds.Top >= 0 && bounds.Bottom <= card.ActualHeight,
                    $"{text.Name} extends outside the card: {bounds}; card height={card.ActualHeight}");
            });
            Assert.True(flavor.TranslatePoint(new Point(), card).Y + flavor.ActualHeight <=
                timer.TranslatePoint(new Point(), card).Y);
            Assert.Equal(((SolidColorBrush)shell.Resources["ToolboxWarningTextBrush"]).Color,
                Assert.IsType<SolidColorBrush>(attendance.Foreground).Color);
            Assert.Equal(((SolidColorBrush)shell.Resources["ToolboxWarningTextBrush"]).Color,
                Assert.IsType<SolidColorBrush>(flavor.Foreground).Color);
            if (state == "holiday") Assert.Contains("国庆节假期余额已清零", vm.Card.FactText);

            var duration = Assert.IsType<TextBlock>(view.FindName("OvertimeDuration"));
            Assert.Equal(vm.OvertimeDurationText, duration.Text);
            Assert.Equal(state == "overtime" ? Visibility.Visible : Visibility.Collapsed,
                Assert.IsType<StackPanel>(view.FindName("OvertimePanel")).Visibility);
            TextBlock[] readingOrder = state == "overtime"
                ? [flavor, timer, attendance, duration, caption]
                : [flavor, timer, attendance, caption];
            double textLeft = flavor.TranslatePoint(new Point(), card).X;
            foreach (TextBlock text in readingOrder)
                Assert.Equal(textLeft, text.TranslatePoint(new Point(), card).X, 1);
            for (int i = 1; i < readingOrder.Length; i++)
            {
                TextBlock previous = readingOrder[i - 1];
                TextBlock next = readingOrder[i];
                Assert.True(previous.TranslatePoint(new Point(), card).Y + previous.ActualHeight <=
                    next.TranslatePoint(new Point(), card).Y,
                    $"{next.Name} must occupy the next row below {previous.Name}.");
            }
            var removedBindings = new[] { "StatusText", "ActiveTaskText", "DateText", "OvertimePeriodText", "OvertimeMessage" };
            Assert.DoesNotContain(Descendants<TextBlock>(card), text =>
                removedBindings.Contains(BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path?.Path));
            Assert.Null(view.FindName("OvertimePeriod"));
            Assert.Null(view.FindName("OvertimeMessage"));
            var companion = Assert.IsType<CountdownCompanion>(view.FindName("CountdownCharacter"));
            Assert.Equal(80, companion.Sticker.ActualWidth);
            Assert.Equal(vm.Reaction.CharacterName, companion.NameText.Text);
            var copy = Assert.IsType<StackPanel>(view.FindName("CountdownCopyPanel"));
            var area = Assert.IsType<Grid>(view.FindName("CountdownCharacterArea"));
            Assert.Equal(20, area.TranslatePoint(new Point(), card).X -
                copy.TranslatePoint(new Point(), card).X - copy.ActualWidth, 1);
            Assert.Equal(area.ActualWidth, companion.ActualWidth, 1);
            Assert.Equal(area.ActualHeight, companion.ActualHeight, 1);
            Assert.True(companion.ActualWidth >= 220 && companion.ActualHeight >= 160);
            Assert.True(double.IsNaN(card.Height), "Card height must follow content without clipping.");
            _output.WriteLine($"state={state}; dark={dark}; card={card.ActualWidth:0.##}x{card.ActualHeight:0.##}; flavor={flavor.Text}; attendance={attendance.Text}");
            WpfTestSnapshot.SaveWindowContent(shell, 760, 640, $"countdown-copy-{state}-{(dark ? "dark" : "light")}.png");
        }
        finally { shell.Close(); }
    });

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
