using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ToolsBox.App.Attendance;

public sealed class AttendanceReminderWindow : Window
{
    private readonly TextBlock _message;
    public AttendanceReminderWindow(Action confirm)
    {
        Title="打卡提醒 · 宝哥工具箱";Width=480;SizeToContent=SizeToContent.Height;
        ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Topmost=true;ShowActivated=false;Background=Brushes.White;
        var panel=new StackPanel{Margin=new Thickness(26)};
        panel.Children.Add(new TextBlock{Text="怎么还不打卡，打算义务上班了嘛。",FontSize=25,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
        _message=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16,0,20),FontSize=15};panel.Children.Add(_message);
        var done=new Button{Content="我已打卡",Padding=new Thickness(20,10,20,10),HorizontalAlignment=HorizontalAlignment.Right};
        done.Click+=(_,_)=>{ try {confirm();Close();}catch(Exception){_message.Text="确认未能保存，请稍后重试。";} };
        panel.Children.Add(done);
        panel.Children.Add(new TextBlock{Text="手动确认仅停止今日提醒，不会修改钉钉记录或生成打卡时间。关闭此窗口会稍后再提醒；09:25 后不再新增提醒。",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,14,0,0)});
        Content=panel;
    }
    public void Update(string text,bool final) => _message.Text=(final?"这是今日最后一次提醒（09:25）。\n\n":"")+text;
}
