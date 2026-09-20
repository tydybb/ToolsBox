using System.Windows;
using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App.Views;

public partial class OffWorkReminderWindow : Window
{
    public OffWorkReminderWindow(WorkCountdownViewModel countdown)
    {
        InitializeComponent();
        DataContext = countdown;
    }

    private void OnDismiss(object sender, RoutedEventArgs e) => Close();
}
