using System.Windows;
using System.Windows.Controls;
using ToolsBox.App.Views;
using ToolsBox.App.WorkCountdown;
using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class CountdownCardIntegrationTests
{
    [Fact]
    public Task Card_UsesAppliedPlanAndDoesNotChangeWithDraftInputs() => WpfTestThread.RunAsync(() =>
    {
        DateTime now = new(2026, 10, 9, 11, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new EmptyStore(), false);
        vm.StartTimeText = "08:30";
        vm.StartCommand.Execute(null);
        CountdownCardPresentation card = vm.Card;
        int notifications = 0;
        vm.PropertyChanged += (_, args) => { if (args.PropertyName == "Card") notifications++; };
        Assert.Contains("明天调休补班", card.FactText);
        vm.DayMode = CountdownDayMode.RestDay;
        vm.OvertimeText = "22";
        Assert.Equal(card, vm.Card);
        now = now.AddSeconds(1);
        vm.Refresh();
        Assert.Equal(card, vm.Card);
        Assert.Equal(0, notifications);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Card_StageChangesAtDeadlineAndFreezesAfterFinish() => WpfTestThread.RunAsync(() =>
    {
        DateTime now = new(2026, 10, 9, 16, 45, 0);
        using var vm = new WorkCountdownViewModel(() => now, new EmptyStore(), false);
        vm.StartTimeText = "08:30";
        vm.StartCommand.Execute(null);
        Assert.Equal("focus", vm.Card.EffectKey);
        now = new(2026, 10, 9, 17, 15, 0);
        vm.Refresh();
        Assert.Equal("rush", vm.Card.EffectKey);
        now = new(2026, 10, 9, 17, 30, 0);
        vm.Refresh();
        Assert.Equal("overdue", vm.Card.EffectKey);
        vm.FinishCommand.Execute(null);
        CountdownCardPresentation finished = vm.Card;
        Assert.Equal("celebrate", finished.EffectKey);
        now = now.AddDays(1);
        vm.Refresh();
        Assert.Equal(finished, vm.Card);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CardView_BindsNewCopyAndKeepsLeftTextRightCharacter(bool dark) => WpfTestThread.RunAsync(() =>
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 11, 5, 11, 0, 0), new EmptyStore(), false);
        vm.StartCommand.Execute(null);
        var shell = new MainWindow(vm, _ => { }, _ => { });
        var view = new WorkCountdownView { Resources = shell.Resources, DataContext = vm };
        shell.Content = view;
        try
        {
            typeof(App).Assembly.GetType("ToolsBox.App.Infrastructure.ComfortAppearance")!
                .GetMethod("Apply")!.Invoke(null, [shell, dark]);
            view.Measure(new Size(760, 640));
            view.Arrange(new Rect(0, 0, 760, 640));
            view.UpdateLayout();
            CountdownCardPresentation card = vm.Card;
            Assert.Equal(card.Headline, Assert.IsType<TextBlock>(view.FindName("FlavorText")).Text);
            Assert.Equal(card.FactText, Assert.IsType<TextBlock>(view.FindName("CountdownDayFacts")).Text);
            Assert.Equal(card.Caption, Assert.IsType<TextBlock>(view.FindName("CountdownCharacterCaption")).Text);
            var companion = Assert.IsType<CountdownCompanion>(view.FindName("CountdownCharacter"));
            Assert.Equal(card.SpeechText, companion.SpeechText);
            Assert.Equal(card.EffectKey, companion.EffectKey);
            var left = Assert.IsType<StackPanel>(view.FindName("CountdownCopyPanel"));
            var right = Assert.IsType<Grid>(view.FindName("CountdownCharacterArea"));
            var hero = Assert.IsType<Border>(view.FindName("CountdownCard"));
            Assert.True(left.TranslatePoint(new Point(left.ActualWidth, 0), hero).X + 19.5 <= right.TranslatePoint(new Point(), hero).X);
            Assert.InRange(hero.ActualHeight, 160, 300);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });

    private sealed class EmptyStore : ICountdownStateStore
    {
        public CountdownState? Load() => null;
        public void Save(CountdownState state) { }
        public void Clear() { }
    }
}
