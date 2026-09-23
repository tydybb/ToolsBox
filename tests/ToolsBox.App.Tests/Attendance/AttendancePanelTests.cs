using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ToolsBox.App.Attendance;

namespace ToolsBox.App.Tests.Attendance;

public class AttendancePanelTests
{
    [Fact]
    public Task StartupStatusIsSeparateFromAttendanceResult() => WpfTestThread.RunAsync(()=>
    {
        var store=new AttendanceStore(Path.Combine(Path.GetTempPath(),"ToolsBox-startup-"+Guid.NewGuid().ToString("N")));
        var view=new AttendancePanel(store,_=>{},()=>{},()=>false);
        Assert.NotNull(view.FindName("AttendanceStartupStatus"));
        Assert.NotNull(view.FindName("AttendanceStartupSettings"));
        return Task.CompletedTask;
    });
    [Fact]
    public Task AutoDetectFillsPathWithoutSilentlySaving() => WpfTestThread.RunAsync(()=>
    {
        string root=Path.Combine(Path.GetTempPath(),"ToolsBox-attendance-panel-"+Guid.NewGuid().ToString("N"));
        var store=new AttendanceStore(root);
        var result=ToolsBox.Core.Attendance.DingTalkLocateResult.Found(new(@"C:\synthetic_v3","unused",null,"unused",null));
        var view=new AttendancePanel(store,_=>{},()=>{},()=>true,()=>result);
        ((Button)view.FindName("AttendanceDetect")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(@"C:\synthetic_v3",((TextBox)view.FindName("AttendancePath")).Text);
        Assert.Equal("",store.LoadOptions().AccountDirectory);
        result=ToolsBox.Core.Attendance.DingTalkLocateResult.Failed("发现多个账号");
        ((Button)view.FindName("AttendanceDetect")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(@"C:\synthetic_v3",((TextBox)view.FindName("AttendancePath")).Text);
        return Task.CompletedTask;
    });
    [Fact]
    public void MainInstanceRejectsDuplicateAndSignalsOriginal()
    {
        string name="Local\\ToolsBox.Test."+Guid.NewGuid().ToString("N");
        using(var first=ToolsBox.App.Infrastructure.MainInstanceGate.Acquire(name))
        {
            Assert.NotNull(first);
            Assert.Null(ToolsBox.App.Infrastructure.MainInstanceGate.Acquire(name));
            Assert.True(first.ConsumeActivation());Assert.False(first.ConsumeActivation());
        }
        using var replacement=ToolsBox.App.Infrastructure.MainInstanceGate.Acquire(name);
        Assert.NotNull(replacement);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task ConfirmedAttendanceDisablesManualButton(bool manual) => WpfTestThread.RunAsync(()=>
    {
        string root=Path.Combine(Path.GetTempPath(),"ToolsBox-attendance-panel-"+Guid.NewGuid().ToString("N"));
        var store=new AttendanceStore(root);
        try
        {
            store.Update(o=>o with{Enabled=true,ManualConfirmedOn=manual?DateOnly.FromDateTime(DateTime.Now):null});
            if(!manual)store.SaveSnapshot(new(DateTime.Now,DateTime.Today,"","已读取"));
            var view=new AttendancePanel(store,_=>{},()=>{},()=>true);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            var panel=(StackPanel)((Border)view.Content).Child;
            var button=panel.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().Last();
            Assert.False(button.IsEnabled);Assert.Contains("已打卡",button.Content.ToString());
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        return Task.CompletedTask;
    });
    [Fact]
    public Task ManualCheckShowsPendingAndResultDespiteManualConfirmation() => WpfTestThread.RunAsync(async ()=>
    {
        string root=Path.Combine(Path.GetTempPath(),"ToolsBox-attendance-panel-"+Guid.NewGuid().ToString("N"));
        var store=new AttendanceStore(root);
        try
        {
            store.Update(o=>o with{Enabled=true,ManualConfirmedOn=DateOnly.FromDateTime(DateTime.Now)});
            int launches=0;
            var view=new AttendancePanel(store,_=>{},()=>launches++,()=>true);
            var panel=(StackPanel)((Border)view.Content).Child;
            var button=panel.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().Single(b=>Equals(b.Content,"立即检查"));
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var status=(TextBlock)view.FindName("AttendanceStatus");
            Assert.Contains("正在请求检查",status.Text);Assert.Equal(1,launches);
            var c=new AttendanceCoordinator(store,(_,_)=>Task.FromResult(ToolsBox.Core.Attendance.AttendanceStatus.Failure("synthetic")));
            await c.CheckAsync(true);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.Contains("synthetic",status.Text);Assert.Contains("自动提醒仍保持停止",status.Text);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ConsentControlsRegistrationAndDisableRemovesOnlyOurFeature(bool consent) => WpfTestThread.RunAsync(()=>
    {
        string root=Path.Combine(Path.GetTempPath(),"ToolsBox-attendance-panel-"+Guid.NewGuid().ToString("N"));
        var store=new AttendanceStore(root);var writes=new List<bool>();int launches=0;
        var panel=new AttendancePanel(store,value=>writes.Add(value),()=>launches++,()=>consent);
        var enabled=(CheckBox)panel.FindName("AttendanceEnabled");
        try
        {
            enabled.IsChecked=true;enabled.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(consent,store.LoadOptions().Enabled);Assert.Equal(consent?1:0,launches);
            if(consent)
            {
                enabled.IsChecked=false;enabled.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.False(store.LoadOptions().Enabled);Assert.Equal(new[]{true,false},writes);
            }
            else Assert.Empty(writes);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        return Task.CompletedTask;
    });

    [Fact]
    public Task FailedLaunchRollsBackOptInAndStartup() => WpfTestThread.RunAsync(()=>
    {
        string root=Path.Combine(Path.GetTempPath(),"ToolsBox-attendance-panel-"+Guid.NewGuid().ToString("N"));
        var store=new AttendanceStore(root);var writes=new List<bool>();
        var panel=new AttendancePanel(store,value=>writes.Add(value),()=>throw new IOException("synthetic"),()=>true);
        try
        {
            var enabled=(CheckBox)panel.FindName("AttendanceEnabled");enabled.IsChecked=true;enabled.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.False(store.LoadOptions().Enabled);Assert.False(enabled.IsChecked);Assert.Equal(new[]{true,false},writes);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        return Task.CompletedTask;
    });
    [Fact]
    public Task ReminderIsTopmostNonblockingAndHasManualConfirmation() => WpfTestThread.RunAsync(()=>
    {
        int confirmed=0;var window=new AttendanceReminderWindow(()=>confirmed++);
        try
        {
            Assert.True(window.Topmost);Assert.False(window.ShowActivated);
            window.Update("尚未确认今日打卡",true);
            window.Measure(new Size(480,600));
            window.Arrange(new Rect(0,0,480,window.DesiredSize.Height));
            window.UpdateLayout();
            var content=(FrameworkElement)window.Content;
            content.Measure(new Size(428,double.PositiveInfinity));
            content.Arrange(new Rect(0,0,428,content.DesiredSize.Height));
            content.UpdateLayout();
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(480,(int)Math.Ceiling(content.ActualHeight)+52,96,96,System.Windows.Media.PixelFormats.Pbgra32);
            var visual=new System.Windows.Media.DrawingVisual();
            using(var drawing=visual.RenderOpen())
            {
                drawing.DrawRectangle(System.Windows.Media.Brushes.White,null,new Rect(0,0,bitmap.Width,bitmap.Height));
                drawing.DrawRectangle(new System.Windows.Media.VisualBrush(content),null,new Rect(26,26,428,content.ActualHeight));
            }
            bitmap.Render(visual);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,"attendance-reminder-preview.png")))encoder.Save(output);
            var panel=(StackPanel)window.Content;
            Assert.Contains("09:25",panel.Children.OfType<TextBlock>().ElementAt(1).Text);
            panel.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(1,confirmed);
        }
        finally{window.Close();}
        return Task.CompletedTask;
    });
}
