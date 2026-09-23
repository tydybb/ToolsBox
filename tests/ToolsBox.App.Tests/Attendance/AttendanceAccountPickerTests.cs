using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ToolsBox.App.Attendance;
using ToolsBox.Core.Attendance;

namespace ToolsBox.App.Tests.Attendance;

public class AttendanceAccountPickerTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public Task DetectButtonOpensOwnedPickerForAnyFoundDirectory(int count) => WpfTestThread.RunAsync(()=>
    {
        var candidates=new[]{new DingTalkAccountPaths(@"C:\synthetic_a_v3","unused",null,"unused",null),new DingTalkAccountPaths(@"C:\synthetic_b_v3","unused",null,"unused",null)};
        var store=new AttendanceStore(Path.Combine(Path.GetTempPath(),"ToolsBox-ui-probe-"+Guid.NewGuid().ToString("N")));
        var panel=new AttendancePanel(store,_=>{},()=>{},()=>false,()=>count==1?DingTalkLocateResult.Found(candidates[0]):DingTalkLocateResult.Failed("multiple") with{Candidates=candidates});
        var owner=new Window{Content=panel,Width=760,Height=600,ShowInTaskbar=false,ShowActivated=false,Left=-10000,Top=-10000};
        bool observed=false;
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
        var elapsed=System.Diagnostics.Stopwatch.StartNew();
        timer.Tick+=(_,_)=>
        {
            var picker=owner.OwnedWindows.OfType<AttendanceAccountPickerWindow>().FirstOrDefault();
            if(picker is not null){observed=picker.IsVisible;picker.Close();}
            if(elapsed.Elapsed>TimeSpan.FromSeconds(5))owner.Close();
        };
        try
        {
            // 失败诊断三连拍：Show 前的 App 状态、Show 后窗口源是否立刻存在、事后 App 状态，用于区分
            // “导航测试并发期间 App 存活”与“导航测试结束后仍有残留”两条投毒路径。
            var appBefore=Application.Current;
            string appAtShow=appBefore is null?"null":$"{(appBefore.Dispatcher.HasShutdownStarted?"shutting":appBefore.Dispatcher.HasShutdownFinished?"dead":"alive")}";
            owner.Show();
            bool sourceAfterShow=PresentationSource.FromVisual(owner) is not null;
            timer.Start();
            ((Button)panel.FindName("AttendanceDetect")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            if(!observed)
            {
                var appAfter=Application.Current;
                string appState=appAfter is null?"null":$"{(appAfter.Dispatcher.HasShutdownStarted?"shutting":appAfter.Dispatcher.HasShutdownFinished?"dead":"alive")}";
                bool ownDispatcherDown=owner.Dispatcher.HasShutdownStarted||owner.Dispatcher.HasShutdownFinished;
                ((TextBlock)panel.FindName("AttendanceStatus")).Text+=$"［诊断］Show时App={appAtShow} 源AfterShow={sourceAfterShow} 事后App={appState} 自身调度器={(ownDispatcherDown?"关停":"正常")} 可见={owner.IsVisible}";
            }
            Assert.True(observed,((TextBlock)panel.FindName("AttendanceStatus")).Text);
            Assert.Equal("",store.LoadOptions().AccountDirectory);
            Assert.Equal("",((TextBox)panel.FindName("AttendancePath")).Text);
        }
        finally{timer.Stop();owner.Close();}
        return Task.CompletedTask;
    });
    [Fact]
    public Task EachPathCanBeCheckedAndSelectedIndependently() => WpfTestThread.RunAsync(()=>
    {
        string[] paths=[@"C:\Users\示例用户\AppData\Roaming\DingTalk\account_a_v3",@"D:\DingTalkData\account_b_v3"];
        string? selected=null;AttendancePreviewResult? applied=null;DateTime now=DateTime.Now;
        var window=new AttendanceAccountPickerWindow(paths.Select(p=>new DingTalkAccountPaths(p,"unused",null,"unused",null)).ToArray(),
            (path,_)=>Task.FromResult(new AttendancePreviewResult(Guid.NewGuid(),path,now,now.Date.AddHours(7),"今日上班打卡：07:00（模拟数据）")),
            (path,result)=>{selected=path;applied=result;});
        try
        {
            var root=(DockPanel)window.Content;
            var rows=(StackPanel)root.Children.OfType<ScrollViewer>().Single().Content;
            Assert.Equal(2,rows.Children.Count);
            var first=(StackPanel)((Border)rows.Children[0]).Child;
            var second=(StackPanel)((Border)rows.Children[1]).Child;
            var buttons=second.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().ToArray();
            buttons[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Contains("07:00",second.Children.OfType<TextBlock>().Last().Text);
            Assert.Equal("尚未检查",first.Children.OfType<TextBlock>().Last().Text);Assert.Null(selected);
            root.Measure(new Size(720,450));root.Arrange(new Rect(0,0,720,450));root.UpdateLayout();
            var bitmap=new RenderTargetBitmap(720,450,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var stream=File.Create(Path.Combine(AppContext.BaseDirectory,"attendance-accounts-preview.png")))encoder.Save(stream);
            buttons[1].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(paths[1],selected);Assert.Equal(paths[1],applied!.Directory);
        }
        finally{window.Close();}
        return Task.CompletedTask;
    });
    [Fact]
    public Task ClosingPendingCheckCancelsAndNeverSelects() => WpfTestThread.RunAsync(async()=>
    {
        var pending=new TaskCompletionSource<AttendancePreviewResult>();CancellationToken observed=default;int selections=0;
        var window=new AttendanceAccountPickerWindow([new(@"C:\synthetic_v3","unused",null,"unused",null)],
            (_,token)=>{observed=token;return pending.Task;},(_,_)=>selections++);
        var root=(DockPanel)window.Content;var rows=(StackPanel)root.Children.OfType<ScrollViewer>().Single().Content;
        var row=(StackPanel)((Border)rows.Children[0]).Child;
        var buttons=row.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().ToArray();
        buttons[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Assert.All(buttons,b=>Assert.False(b.IsEnabled));
        window.Close();Assert.True(observed.IsCancellationRequested);
        pending.SetResult(new(Guid.NewGuid(),@"C:\synthetic_v3",DateTime.Now,DateTime.Now,"late"));
        await Task.Yield();Assert.Equal(0,selections);
    });
}
