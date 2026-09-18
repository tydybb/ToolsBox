using System.Windows;
using System.Windows.Controls;
using ToolsBox.Windows.ArchiveRecovery;

namespace ToolsBox.App.Views;

public partial class ArchiveRecoveryView : UserControl
{
    public ArchiveRecoveryView() => InitializeComponent();
    private void OnLicensesClick(object sender, RoutedEventArgs e)
    {
        new Window
        {
            Title = "内置引擎与第三方许可", Width = 800, Height = 600,
            Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBox { Text = EmbeddedArchiveEngine.ReadLicenseNotices(), IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) }
        }.ShowDialog();
    }
}
