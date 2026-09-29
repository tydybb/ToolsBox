using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ToolsBox.App.Lottery;

namespace ToolsBox.App.Views;

public partial class LotteryView : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(5) };
    public LotteryView()
    {
        InitializeComponent();
        _timer.Tick += Refresh;
        IsVisibleChanged += (_, _) => UpdateTimer();
        foreach (TextBox input in new[] { Red1, Red2, Red3, Red4, Red5, Red6, BlueNumber })
            DataObject.AddPastingHandler(input, OnBallPaste);
    }
    private void OnBallTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = e.Text.Any(c => c is < '0' or > '9');
    private void OnBallFocus(object sender, KeyboardFocusChangedEventArgs e) => ((TextBox)sender).SelectAll();
    private void OnBallTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox input || !input.IsKeyboardFocused || input.Name == "BlueNumber" || input.Text.Length != 2) return;
        if (int.TryParse(input.Text, out int number) && number is >= 1 and <= 33)
            input.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }
    private void OnBallPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox input || e.DataObject.GetData(DataFormats.UnicodeText) is not string text ||
            text.Any(c => c is < '0' or > '9') || input.Text.Length - input.SelectionLength + text.Length > 2)
            e.CancelCommand();
    }
    private async void OnModeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || sender is not DataGridCell cell || cell.Column != ModeColumn ||
            cell.DataContext is not LotteryRecordRow row || DataContext is not LotteryViewModel vm) return;
        e.Handled = true;
        await vm.ToggleModeAsync(row.Settlement.Purchase.Id);
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateTimer();
        if (IsVisible && DataContext is LotteryViewModel vm) await vm.RefreshAsync();
    }
    private void OnUnloaded(object sender, RoutedEventArgs e) => _timer.Stop();
    private void UpdateTimer()
    {
        if (IsLoaded && IsVisible) _timer.Start();
        else _timer.Stop();
    }
    private async void Refresh(object? sender, EventArgs e)
    {
        if (IsLoaded && IsVisible && DataContext is LotteryViewModel vm) await vm.RefreshAsync();
    }
}
