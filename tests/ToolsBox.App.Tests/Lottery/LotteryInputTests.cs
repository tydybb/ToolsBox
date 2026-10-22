using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ToolsBox.App.Lottery;
using ToolsBox.App.Views;
using ToolsBox.Core.Lottery;

namespace ToolsBox.App.Tests.Lottery;

public sealed class LotteryInputTests
{
    [Fact]
    public Task FavoriteNameDialogValidatesThenAcceptsTrimmedName() => WpfTestThread.RunAsync(() =>
    {
        var dialog = new LotteryFavoriteNameDialog("");
        dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var save = Find<Button>(dialog).Single(b => b.IsDefault);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(dialog.IsVisible);
            Find<TextBox>(dialog).Single().Text = "  我的号码  ";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        try { Assert.True(dialog.ShowDialog()); Assert.Equal("我的号码",dialog.FavoriteName); }
        finally { dialog.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public async Task FailedRefreshStillUpdatesExpiredDrawHint()
    {
        using var fixture = new Fixture();
        fixture.Store.Save(new() { Draws = [new("2026112", new(2026,9,27), LotteryNumbers.Parse("1 2 3 4 5 6 + 7"), new Dictionary<int,decimal>(), false)] });
        var now = new DateTimeOffset(2026,9,29,9,0,0,TimeSpan.FromHours(8));
        using var vm = new LotteryViewModel(fixture.Store,new OfflineClient(),_=>true,()=>now,_=>null);
        Assert.Contains("21:15",vm.NextDrawStatus);
        bool notified = false;
        vm.PropertyChanged += (_,e) => { if (e.PropertyName == nameof(vm.NextDrawStatus)) notified=true; };
        now = now.AddDays(1);
        await vm.RefreshAsync();
        Assert.True(notified);
        Assert.Contains("待更新确认", vm.NextDrawStatus);
    }

    [Fact]
    public void FavoriteUsesManualNumbersNotSelectedMachinePick()
    {
        using var fixture = new Fixture();
        using var vm = fixture.Create(_ => "手动幸运号");
        vm.RedInput = "1 2 3 4 5 6"; vm.BlueInput = "7";
        vm.SelectedDraft = LotteryNumbers.Parse("10 11 12 13 14 15 + 16");
        vm.SaveFavoriteCommand.Execute(null);
        Assert.Equal("01 02 03 04 05 06 + 07", Assert.Single(vm.Favorites).Numbers.ToString());
        vm.RedInput = "";
        vm.SaveFavoriteCommand.Execute(null);
        Assert.Single(vm.Favorites);
    }

    [Fact]
    public void CancelFavoriteNameDoesNotSave()
    {
        using var fixture = new Fixture();
        using var vm = fixture.Create(_ => null);
        vm.RedInput = "1 2 3 4 5 6"; vm.BlueInput = "7";
        vm.SaveFavoriteCommand.Execute(null);
        Assert.Empty(vm.Favorites);
    }

    [Fact]
    public void AggregateTotalExcludesPendingAndNextDrawShowsChinaTime()
    {
        using var fixture = new Fixture();
        fixture.Store.Save(new() { Draws = [new("2026112", new(2026,9,27), LotteryNumbers.Parse("1 2 3 4 5 6 + 7"), new Dictionary<int,decimal> { [6] = 5 }, false)],
            Purchases = [new(Guid.NewGuid(), "2026112", LotteryNumbers.Parse("20 21 22 23 24 25 + 8"),1,LotteryBuyMode.Real,DateTimeOffset.Now),
                new(Guid.NewGuid(), "2026112", LotteryNumbers.Parse("20 21 22 23 24 25 + 7"),1,LotteryBuyMode.Fake,DateTimeOffset.Now),
                new(Guid.NewGuid(), "2026113", LotteryNumbers.Parse("20 21 22 23 24 25 + 7"),99,LotteryBuyMode.Fake,DateTimeOffset.Now)] });
        using var vm = fixture.Create(_ => null);
        Assert.Equal(-5m, vm.TotalNet);
        Assert.Contains("非现金收益", vm.TotalSummary);
        Assert.Contains("2026-09-29 21:15", vm.NextDrawStatus);
        Assert.Contains("北京时间", vm.NextDrawStatus);
    }

    [Fact]
    public Task TwoDigitsMoveFocusButOneDigitAndInvalidRangeDoNot() => WpfTestThread.RunAsync(async () =>
    {
        using var fixture = new Fixture();
        fixture.Store.Save(new() { Draws = [new("2026112", new(2026,9,27), LotteryNumbers.Parse("1 2 3 4 5 6 + 7"), new Dictionary<int,decimal>(),false)],
            Purchases = [new(Guid.NewGuid(),"2026112",LotteryNumbers.Parse("10 11 12 13 14 15 + 16"),1,LotteryBuyMode.Fake,DateTimeOffset.Now)],
            Favorites = [new("示例幸运号",LotteryNumbers.Parse("2 4 6 8 10 12 + 9"))] });
        using var vm = fixture.Create(_ => "幸运号");
        var view = new LotteryView { DataContext = vm };
        var window = new Window { Content = view, Width = 1060, Height = 850, ShowInTaskbar = false };
        try
        {
            window.Show(); window.Activate();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var first = Assert.IsType<TextBox>(view.FindName("Red1"));
            var second = Assert.IsType<TextBox>(view.FindName("Red2"));
            Keyboard.Focus(first); first.Text = "0";
            Assert.True(first.IsKeyboardFocused);
            first.Text = "01";
            Assert.True(second.IsKeyboardFocused);
            second.Text = "99";
            Assert.True(second.IsKeyboardFocused);
            var sixth = Assert.IsType<TextBox>(view.FindName("Red6"));
            Keyboard.Focus(sixth); sixth.Text = "33";
            Assert.True(Assert.IsType<TextBox>(view.FindName("BlueNumber")).IsKeyboardFocused);
            var invalidText = new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current,first,"x"))
                { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
            first.RaiseEvent(invalidText);
            Assert.True(invalidText.Handled);
            var paste = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText,"123"),false,DataFormats.UnicodeText)
                { RoutedEvent = DataObject.PastingEvent };
            first.RaiseEvent(paste);
            Assert.True(paste.CommandCancelled);
            WpfTestSnapshot.SaveWindowContent(window,1060,850,"lottery-input-refined.png");
            var tabs = Find<TabControl>(view).Single();
            foreach (int index in new[] { 1, 2, 3 })
            {
                tabs.SelectedIndex = index;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var coloredBalls = Find<Border>(view).Where(b => b.CornerRadius.TopLeft == 13 &&
                    b.Background is System.Windows.Media.SolidColorBrush color && (color.Color.ToString() == "#FFD94351" || color.Color.ToString() == "#FF246BFD")).ToArray();
                Assert.Equal(7,coloredBalls.Length);
                WpfTestSnapshot.SaveWindowContent(window,1060,850,$"lottery-balls-tab-{index}.png");
            }
            tabs.SelectedIndex = 3;
            window.Width = 760; window.Height = 530;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var records = Find<DataGrid>(view).Single();
            Assert.True(records.ActualHeight >= 150, $"记录表高度仅 {records.ActualHeight}");
            records.BringIntoView();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Contains(Find<ScrollViewer>(view), scroll => scroll.VerticalOffset > 0);
            WpfTestSnapshot.SaveWindowContent(window,760,530,"lottery-ledger-minimum.png");
        }
        finally { window.Close(); }
    });

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=System.Windows.Media.VisualTreeHelper.GetChild(root,i);
            if(child is T item) yield return item;
            foreach(var descendant in Find<T>(child)) yield return descendant;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "lottery-input-" + Guid.NewGuid());
        public LotteryStore Store => new(Path.Combine(_root,"state.json"));
        public LotteryViewModel Create(Func<string,string?> name) => new(Store,new OfflineClient(), _=>true,
            () => new DateTimeOffset(2026,9,29,9,0,0,TimeSpan.FromHours(8)), name);
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root,true); }
    }
    private sealed class OfflineClient : ILotteryClient
    {
        public Task<IReadOnlyList<LotteryDraw>> FetchAsync(bool allHistory,IProgress<string>? progress,CancellationToken token) =>
            Task.FromException<IReadOnlyList<LotteryDraw>>(new IOException("离线测试"));
    }
}
