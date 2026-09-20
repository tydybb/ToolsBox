using System.Windows;
using ToolsBox.Core.FileUnlocking;

namespace ToolsBox.App.Views;

public partial class ProcessTerminationDialog : Window
{
    public ProcessTerminationDialog(string targetPath, IReadOnlyList<FileLockEntry> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        InitializeComponent();
        TargetPathText.Text = targetPath;
        SummaryText.Text = $"将强制结束以下 {targets.Count} 个进程，不递归终止它们的子进程。清单可滚动查看。";
        ProcessList.ItemsSource = targets;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (RiskAcknowledgement.IsChecked == true) DialogResult = true;
    }
}
