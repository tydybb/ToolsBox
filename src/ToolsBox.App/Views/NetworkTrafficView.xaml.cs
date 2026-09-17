using System.Windows;
using System.Windows.Controls;
using ToolsBox.App.NetworkTraffic;

namespace ToolsBox.App.Views;

public partial class NetworkTrafficView : UserControl
{
    public NetworkTrafficView() => InitializeComponent();

    private NetworkTrafficViewModel ViewModel => (NetworkTrafficViewModel)DataContext;

    private void OnToggleDetails(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ApplicationTrafficItemViewModel item })
        {
            item.IsExpanded = !item.IsExpanded;
        }
    }

    private async void OnEditLimit(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ApplicationTrafficItemViewModel item })
        {
            return;
        }

        var dialog = new BandwidthLimitWindow(item)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            if (dialog.RemoveRequested)
            {
                await ViewModel.RemoveUploadLimitAsync(item);
            }
            else
            {
                await ViewModel.SetUploadLimitAsync(item, dialog.LimitValue, dialog.Unit);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "上传限速操作失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
