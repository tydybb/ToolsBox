using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ToolsBox.App.FileUnlocking;

namespace ToolsBox.App.Views;

public partial class FileUnlockerView : UserControl
{
    public FileUnlockerView() => InitializeComponent();

    private FileUnlockerViewModel ViewModel => (FileUnlockerViewModel)DataContext;

    private async void OnBrowseFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog() == true)
        {
            await ViewModel.SetPathAndScanAsync(dialog.FileName);
        }
    }

    private async void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Multiselect = false, Title = "选择需要检测占用的文件夹" };
        if (dialog.ShowDialog() == true)
        {
            await ViewModel.SetPathAndScanAsync(dialog.FolderName);
        }
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = !ViewModel.IsBusy && HasSinglePath(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            await ViewModel.HandleDroppedPathsAsync(paths);
        }
    }

    private static bool HasSinglePath(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 };

    private async void OnTerminateProcesses(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanActOnSelectedEntries) return;
        if (MessageBox.Show($"已选 {ViewModel.SelectedCount} 条占用记录，将结束涉及的 {ViewModel.SelectedProcessCount} 个进程，进程中未保存的数据会丢失。是否继续？", "确认结束进程",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        string summary = await ViewModel.TerminateSelectedProcessesAsync();
        MessageBox.Show(summary, "操作结果", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void OnCloseHandles(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanActOnSelectedEntries) return;
        if (MessageBox.Show($"这是高风险操作。将强制关闭 {ViewModel.SelectedCount} 个句柄，涉及 {ViewModel.SelectedProcessCount} 个进程，可能导致目标程序崩溃或文件损坏。确定继续？", "强制关闭句柄",
                MessageBoxButton.YesNo, MessageBoxImage.Stop) != MessageBoxResult.Yes)
        {
            return;
        }

        string summary = await ViewModel.CloseSelectedHandlesAsync();
        MessageBox.Show(summary, "操作结果", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
