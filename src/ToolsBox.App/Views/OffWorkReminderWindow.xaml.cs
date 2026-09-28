using System.ComponentModel;
using System.Windows;
using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App.Views;

public partial class OffWorkReminderWindow : Window
{
    private readonly WorkCountdownViewModel _countdown;
    private bool _closed;

    public OffWorkReminderWindow(WorkCountdownViewModel countdown)
    {
        InitializeComponent();
        DataContext = countdown;
        _countdown = countdown;
        // 点击本窗的“下班”（或倒计时卡片上的“下班”）后，这扇“即将关机”警告已无意义，随任务结束自动关闭。
        _countdown.PropertyChanged += OnCountdownPropertyChanged;
        Closed += (_, _) => { _closed = true; _countdown.PropertyChanged -= OnCountdownPropertyChanged; };
    }

    private void OnCountdownPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 主窗口在 IsOverdue 变 false 时也会关一次本窗，这里只负责补位（后台代理宿主没有该逻辑）。
        if (_closed || e.PropertyName != nameof(WorkCountdownViewModel.IsFinished) || !_countdown.IsFinished) return;
        Close();
    }

    private void OnDismiss(object sender, RoutedEventArgs e) => Close();
}
