using System.IO;
using ToolsBox.App.Lottery;
using ToolsBox.Core.Lottery;

namespace ToolsBox.App.Tests.Lottery;

public sealed class LotteryModeTests
{
    [Fact]
    public Task DoubleClickOnlyModeCellChangesMode() => WpfTestThread.RunAsync(async () =>
    {
        using var f=new Fixture();using var vm=f.Model();
        var view=new ToolsBox.App.Views.LotteryView {DataContext=vm};
        var window=new System.Windows.Window {Content=view,Width=1050,Height=850,ShowInTaskbar=false};
        try
        {
            window.Show();
            var tabs=Find<System.Windows.Controls.TabControl>(view).Single();tabs.SelectedIndex=3;
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var cells=Find<System.Windows.Controls.DataGridCell>(view).ToArray();
            var issue=cells.First(c=>Equals(c.Column.Header,"期号"));
            var mode=cells.First(c=>Equals(c.Column.Header,"模式"));
            void DoubleClick(System.Windows.Controls.DataGridCell cell)=>cell.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,System.Windows.Input.MouseButton.Left)
                {RoutedEvent=System.Windows.Controls.Control.MouseDoubleClickEvent});
            DoubleClick(issue); Assert.Equal(0,f.ConfirmCalls);
            DoubleClick(mode);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.Equal(1,f.ConfirmCalls);
            Assert.Equal("真买",Assert.Single(vm.Records).Mode);
        }
        finally{window.Close();}
    });

    [Fact]
    public Task FiveDraftRowsFitAtMinimumWidth() => WpfTestThread.RunAsync(async () =>
    {
        using var f=new Fixture();using var vm=f.Model();
        var view=new ToolsBox.App.Views.LotteryView {DataContext=vm};
        var window=new System.Windows.Window {Content=view,Width=760,Height=530,ShowInTaskbar=false};
        try
        {
            window.Show();await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            vm.GenerateCommand.Execute(null);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var list=Find<System.Windows.Controls.ListBox>(view).Single();
            var fifth=Assert.IsType<System.Windows.Controls.ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(4));
            double bottom=fifth.TransformToAncestor(list).Transform(new System.Windows.Point(0,fifth.ActualHeight)).Y;
            Assert.True(bottom<=list.ActualHeight-2,$"第五注底部 {bottom} 超出列表高度 {list.ActualHeight}");
            list.BringIntoView();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            WpfTestSnapshot.SaveWindowContent(window,760,530,"lottery-five-picks.png");
        }
        finally{window.Close();}
    });

    [Fact]
    public Task TextCellsAreVerticallyCentered() => WpfTestThread.RunAsync(async () =>
    {
        using var f=new Fixture();using var vm=f.Model();
        var view=new ToolsBox.App.Views.LotteryView {DataContext=vm};
        var window=new System.Windows.Window {Content=view,Width=1050,Height=850,ShowInTaskbar=false};
        try
        {
            window.Show();var tabs=Find<System.Windows.Controls.TabControl>(view).Single();
            foreach(int index in new[]{2,3})
            {
                tabs.SelectedIndex=index;await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var cells=Find<System.Windows.Controls.DataGridCell>(view).Where(c=>c.Column is System.Windows.Controls.DataGridTextColumn).ToArray();
                Assert.NotEmpty(cells);
                foreach(var cell in cells)
                {
                    var text=Find<System.Windows.Controls.TextBlock>(cell).Single();
                    Assert.Equal(System.Windows.VerticalAlignment.Center,text.VerticalAlignment);
                    double top=text.TransformToAncestor(cell).Transform(new System.Windows.Point()).Y;
                    Assert.InRange(Math.Abs(top+text.ActualHeight/2-cell.ActualHeight/2),0,1.5);
                }
                WpfTestSnapshot.SaveWindowContent(window,1050,850,$"lottery-centered-{index}.png");
            }
        }
        finally{window.Close();}
    });

    private static IEnumerable<T> Find<T>(System.Windows.DependencyObject root) where T:System.Windows.DependencyObject
    {
        for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=System.Windows.Media.VisualTreeHelper.GetChild(root,i);
            if(child is T item)yield return item;
            foreach(var nested in Find<T>(child))yield return nested;
        }
    }
    [Fact]
    public async Task TogglePersistsOnlyModeAndUpdatesOutlay()
    {
        using var f = new Fixture();
        using var vm = f.Model();
        await vm.ToggleModeAsync(f.Purchase.Id);
        var changed = Assert.Single(f.Store.Load().Purchases);
        Assert.Equal(LotteryBuyMode.Real, changed.Mode);
        Assert.Equal(f.Purchase with { Mode = LotteryBuyMode.Real }, changed with { Numbers = f.Purchase.Numbers });
        Assert.Contains("2.00", vm.RealSummary);
        Assert.Equal("真买", Assert.Single(vm.Records).Mode);
        Assert.Equal(0m,vm.TotalNet);
        await vm.ToggleModeAsync(f.Purchase.Id);
        Assert.Equal(LotteryBuyMode.Fake,Assert.Single(f.Store.Load().Purchases).Mode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DrawnIsLockedEvenWhenPrizeMissing(bool published)
    {
        using var f = new Fixture();
        f.State.Draws.Add(new("2026113",new(2026,9,29),f.Purchase.Numbers,
            published ? new Dictionary<int,decimal> { [1]=5000000 } : new Dictionary<int,decimal>(),false));
        f.Store.Save(f.State);
        using var vm=f.Model();
        await vm.ToggleModeAsync(f.Purchase.Id);
        Assert.Equal(0,f.ConfirmCalls);
        Assert.Equal(LotteryBuyMode.Fake,Assert.Single(f.Store.Load().Purchases).Mode);
        Assert.Contains("已开奖",vm.Status);
    }

    [Fact]
    public async Task NewlyFetchedDrawBlocksChange()
    {
        using var f=new Fixture();
        f.Client.Draws=[..f.State.Draws,new("2026113",new(2026,9,29),f.Purchase.Numbers,new Dictionary<int,decimal>(),false)];
        using var vm=f.Model();
        await vm.ToggleModeAsync(f.Purchase.Id);
        Assert.Equal(LotteryBuyMode.Fake,Assert.Single(f.Store.Load().Purchases).Mode);
        Assert.Contains("已开奖",vm.Status);
    }

    [Fact]
    public async Task CancelOrOfflineDoesNotChangeMode()
    {
        using var f=new Fixture();
        f.ConfirmResult=false;
        using var vm=f.Model();
        await vm.ToggleModeAsync(f.Purchase.Id);
        Assert.Equal(0,f.Client.Calls);
        f.ConfirmResult=true;f.Client.Fail=true;
        await vm.ToggleModeAsync(f.Purchase.Id);
        Assert.Equal(LotteryBuyMode.Fake,Assert.Single(f.Store.Load().Purchases).Mode);
        Assert.Equal("假买",Assert.Single(vm.Records).Mode);
    }

    [Fact]
    public async Task CrossingScheduledDrawTimeWhileConfirmingBlocksChange()
    {
        using var f=new Fixture();
        f.OnConfirm=()=>f.Now=new(2026,9,29,21,15,0,TimeSpan.FromHours(8));
        using var vm=f.Model();
        await vm.ToggleModeAsync(f.Purchase.Id);
        Assert.Equal(LotteryBuyMode.Fake,Assert.Single(f.Store.Load().Purchases).Mode);
        Assert.Contains("开奖",vm.Status);
    }

    [Fact]
    public async Task WriteFailureLeavesInMemoryModeUntouched()
    {
        using var f=new Fixture();
        using var vm=f.Model();
        f.OnConfirm=()=> { File.Delete(f.Path); Directory.CreateDirectory(f.Path); };
        await vm.ToggleModeAsync(f.Purchase.Id);
        Assert.Equal("假买",Assert.Single(vm.Records).Mode);
        Assert.Contains("失败",vm.Status);
    }

    private sealed class Fixture:IDisposable
    {
        private readonly string _root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"lottery-mode-"+Guid.NewGuid());
        public string Path=>System.IO.Path.Combine(_root,"state.json");
        public LotteryStore Store=>new(Path);
        public LotteryPurchase Purchase{get;}=new(Guid.NewGuid(),"2026113",LotteryNumbers.Parse("1 2 3 4 5 6 + 7"),1,LotteryBuyMode.Fake,new DateTimeOffset(2026,9,29,9,0,0,TimeSpan.FromHours(8)));
        public LotteryState State{get;}
        public Client Client{get;}=new();
        public DateTimeOffset Now=new(2026,9,29,10,0,0,TimeSpan.FromHours(8));
        public int ConfirmCalls;
        public bool ConfirmResult=true;
        public Action? OnConfirm;
        public Fixture()
        {
            State=new(){Draws=[new("2026112",new(2026,9,27),LotteryNumbers.Parse("1 2 3 4 5 6 + 7"),new Dictionary<int,decimal>(),false)],Purchases=[Purchase]};
            Store.Save(State);Client.Draws=State.Draws.ToArray();
        }
        public LotteryViewModel Model()=>new(Store,Client,_=>{ConfirmCalls++;OnConfirm?.Invoke();return ConfirmResult;},()=>Now,_=>null);
        public void Dispose(){if(Directory.Exists(_root))Directory.Delete(_root,true);}
    }
    private sealed class Client:ILotteryClient
    {
        public IReadOnlyList<LotteryDraw> Draws=[];
        public bool Fail;
        public int Calls;
        public Task<IReadOnlyList<LotteryDraw>> FetchAsync(bool allHistory,IProgress<string>? progress,CancellationToken token)
        {Calls++;return Fail?Task.FromException<IReadOnlyList<LotteryDraw>>(new IOException("离线")):Task.FromResult(Draws);}
    }
}
