using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ToolsBox.Core.Attendance;

namespace ToolsBox.App.Attendance;

public sealed class AttendanceAccountPickerWindow : Window
{
    private readonly CancellationTokenSource _closed=new();
    private readonly List<Button> _buttons=[];
    private bool _busy;
    public AttendanceAccountPickerWindow(IReadOnlyList<DingTalkAccountPaths> candidates,
        Func<string,CancellationToken,Task<AttendancePreviewResult>> check,
        Action<string,AttendancePreviewResult?> select)
    {
        Title="选择钉钉账号数据目录";Width=760;Height=520;MinWidth=540;MinHeight=350;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(244,246,249));
        var root=new DockPanel{Margin=new Thickness(20),Background=Background};
        var hint=new TextBlock{Text=$"发现 {candidates.Count} 个账号目录，请确认是你本人使用的账号。\n逐项检查不会切换当前账号；点击“确定路径”才保存并导入有效打卡时间。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)};
        DockPanel.SetDock(hint,Dock.Top);root.Children.Add(hint);
        var rows=new StackPanel();
        foreach(var candidate in candidates)
        {
            string path=candidate.AccountDir;
            AttendancePreviewResult? result=null;
            var row=new StackPanel();
            row.Children.Add(new TextBlock{Text=path,TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold,ToolTip=path});
            var status=new TextBlock{Text="尚未检查",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10),Foreground=Brushes.DimGray};row.Children.Add(status);
            var actions=new WrapPanel();
            var inspect=new Button{Content="立即检查",Padding=new Thickness(14,7,14,7),Margin=new Thickness(0,0,10,0)};
            var choose=new Button{Content="确定路径",Padding=new Thickness(14,7,14,7)};
            _buttons.Add(inspect);_buttons.Add(choose);
            inspect.Click+=async(_,_)=>
            {
                if(_busy || _closed.IsCancellationRequested)return;
                _busy=true;_buttons.ForEach(b=>b.IsEnabled=false);status.Text="正在检查今日打卡…";result=null;
                try
                {
                    var found=await check(path,_closed.Token);
                    if(_closed.IsCancellationRequested)return;
                    if(found.Directory!=path)throw new InvalidOperationException("检查结果目录不匹配，请重试。");
                    result=found;status.Text=found.Message;
                }
                catch(OperationCanceledException){ }
                catch(Exception error){if(!_closed.IsCancellationRequested)status.Text=error.Message;}
                finally{_busy=false;if(!_closed.IsCancellationRequested)_buttons.ForEach(b=>b.IsEnabled=true);}
            };
            choose.Click+=(_,_)=>
            {
                if(_busy || _closed.IsCancellationRequested)return;
                try{select(path,result);Close();}
                catch(Exception error){status.Text="保存未完成："+error.Message;}
            };
            actions.Children.Add(inspect);actions.Children.Add(choose);row.Children.Add(actions);
            rows.Children.Add(new Border{Child=row,Background=Brushes.White,CornerRadius=new CornerRadius(8),Padding=new Thickness(16),Margin=new Thickness(0,0,0,12)});
        }
        root.Children.Add(new ScrollViewer{Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        Content=root;
        Closed+=(_,_)=>_closed.Cancel();
    }
}
