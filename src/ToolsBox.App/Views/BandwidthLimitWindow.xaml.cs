using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ToolsBox.App.NetworkTraffic;
using ToolsBox.Core.NetworkTraffic;

namespace ToolsBox.App.Views;

public partial class BandwidthLimitWindow : Window
{
    public BandwidthLimitWindow(ApplicationTrafficItemViewModel item)
    {
        InitializeComponent();
        ApplicationText.Text = $"{item.ApplicationName}  ·  {item.ExecutablePath}";
    }

    public decimal LimitValue { get; private set; } = 1;
    public BandwidthUnit Unit { get; private set; } = BandwidthUnit.MegabytesPerSecond;
    public bool RemoveRequested { get; private set; }

    private void OnPreset(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag })
        {
            return;
        }

        string[] parts = tag.Split('|');
        ValueTextBox.Text = parts[0];
        UnitComboBox.SelectedIndex = string.Equals(parts[1], "KB", StringComparison.Ordinal) ? 0 : 1;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(ValueTextBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal value) || value <= 0)
        {
            MessageBox.Show("请输入大于零的限速值。", "输入无效", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LimitValue = value;
        Unit = UnitComboBox.SelectedIndex == 0
            ? BandwidthUnit.KilobytesPerSecond
            : BandwidthUnit.MegabytesPerSecond;
        DialogResult = true;
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        RemoveRequested = true;
        DialogResult = true;
    }
}
