using System.IO;
using System.Net.Http;
using ToolsBox.App.Lottery;
using ToolsBox.Core.Lottery;

namespace ToolsBox.App.Tests.Lottery;

public sealed class LotteryViewModelTests
{
    [Fact]
    public async Task OldPendingRecordTriggersFullHistoryCatchUp()
    {
        string path = Path.Combine(Path.GetTempPath(), "lottery-test-" + Guid.NewGuid(), "state.json");
        var store = new LotteryStore(path);
        store.Save(new() { Purchases = [new(Guid.NewGuid(), "2026001", LotteryNumbers.Parse("1 2 3 4 5 6 + 7"), 1,
            LotteryBuyMode.Fake, new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.FromHours(8)))] });
        var client = new FakeClient();
        using var vm = new LotteryViewModel(store, client, _ => true);
        try
        {
            await vm.RefreshAsync();
            Assert.Equal(1, client.FullCalls);
            Assert.Equal("1 等奖", Assert.Single(vm.Records).Result);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }
    [Fact]
    public Task FourTabsRenderAndMachinePickIsVisible() => WpfTestThread.RunAsync(async () =>
    {
        string path = Path.Combine(Path.GetTempPath(), "lottery-test-" + Guid.NewGuid(), "state.json");
        using var vm = new LotteryViewModel(new LotteryStore(path), new FakeClient(), _ => true,
            () => new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.FromHours(8)));
        var view = new ToolsBox.App.Views.LotteryView { DataContext = vm };
        var window = new System.Windows.Window { Content = view, Width = 1050, Height = 790, ShowInTaskbar = false };
        try
        {
            window.Show();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            vm.GenerateCommand.Execute(null);
            Assert.Equal(5, vm.Draft.Count);
            Assert.Equal("2026113", vm.NextIssue);
            var tabs = Find<System.Windows.Controls.TabControl>(view).Single();
            Assert.Equal(4, tabs.Items.Count);
            WpfTestSnapshot.SaveWindowContent(window, 1050, 790, "lottery-picks.png");
            tabs.SelectedIndex = 3;
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            WpfTestSnapshot.SaveWindowContent(window, 1050, 790, "lottery-ledger.png");
            var timer = (System.Windows.Threading.DispatcherTimer)typeof(ToolsBox.App.Views.LotteryView)
                .GetField("_timer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
            Assert.True(timer.IsEnabled);
            window.Hide();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.False(timer.IsEnabled);
            window.Show();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.True(timer.IsEnabled);
        }
        finally { window.Close(); if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true); }
    });

    private static IEnumerable<T> Find<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Find<T>(child)) yield return descendant;
        }
    }

    [Fact]
    public async Task FakePurchasePersistsAndRefreshDoesNotDuplicateIt()
    {
        string path = Path.Combine(Path.GetTempPath(), "lottery-test-" + Guid.NewGuid(), "state.json");
        var client = new FakeClient();
        using var vm = new LotteryViewModel(new LotteryStore(path), client, _ => true,
            () => new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.FromHours(8)));
        try
        {
            await vm.RefreshAsync();
            vm.RedInput = "01 02 03 04 05 06"; vm.BlueInput = "07";
            vm.AddManualCommand.Execute(null);
            Assert.Single(vm.Draft);
            vm.ScheduleConfirmed = true;
            await vm.BuyAsync();
            Assert.Single(vm.Records);
            Assert.Empty(vm.Draft);
            Assert.Single(new LotteryStore(path).Load().Purchases);
            await vm.RefreshAsync();
            Assert.Single(vm.Records);
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public async Task NetworkFailureKeepsDraftAndDoesNotSaveBuy()
    {
        string path = Path.Combine(Path.GetTempPath(), "lottery-test-" + Guid.NewGuid(), "state.json");
        var client = new FakeClient();
        using var vm = new LotteryViewModel(new LotteryStore(path), client, _ => true,
            () => new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.FromHours(8)));
        try
        {
            await vm.RefreshAsync();
            vm.GenerateCommand.Execute(null); vm.ScheduleConfirmed = true;
            client.Fail = true;
            await vm.BuyAsync();
            Assert.NotEmpty(vm.Draft);
            Assert.Empty(vm.Records);
            Assert.Contains("失败", vm.Status);
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void InvalidSelfPickAndDuplicateFavoriteAreRejected()
    {
        string path = Path.Combine(Path.GetTempPath(), "lottery-test-" + Guid.NewGuid(), "state.json");
        using var vm = new LotteryViewModel(new LotteryStore(path), new FakeClient(), _ => true, requestFavoriteName: _ => "我的幸运号码");
        try
        {
            vm.RedInput = "1 1 2 3 4 5"; vm.BlueInput = "16";
            vm.AddManualCommand.Execute(null); Assert.Empty(vm.Draft);
            vm.RedInput = "1 2 3 4 5 6";
            vm.AddManualCommand.Execute(null); vm.SelectedDraft = Assert.Single(vm.Draft);
            vm.SaveFavoriteCommand.Execute(null); vm.SaveFavoriteCommand.Execute(null);
            Assert.Single(vm.Favorites);
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    private sealed class FakeClient : ILotteryClient
    {
        public bool Fail { get; set; }
        public int FullCalls { get; private set; }
        public Task<IReadOnlyList<LotteryDraw>> FetchAsync(bool allHistory, IProgress<string>? progress, CancellationToken token)
        {
            if (Fail) throw new HttpRequestException("测试网络故障");
            if (allHistory)
            {
                FullCalls++;
                return Task.FromResult<IReadOnlyList<LotteryDraw>>([new("2026112", new(2026, 9, 27), LotteryNumbers.Parse("1 2 3 4 5 6 + 7"), new Dictionary<int, decimal> { [1] = 5000000 }, false),
                    new("2026001", new(2026, 1, 1), LotteryNumbers.Parse("1 2 3 4 5 6 + 7"), new Dictionary<int, decimal> { [1] = 5000000 }, false)]);
            }
            return Task.FromResult<IReadOnlyList<LotteryDraw>>([new("2026112", new(2026, 9, 27),
                LotteryNumbers.Parse("1 2 3 4 5 6 + 7"), new Dictionary<int, decimal> { [1] = 5000000 }, false)]);
        }
    }
}
