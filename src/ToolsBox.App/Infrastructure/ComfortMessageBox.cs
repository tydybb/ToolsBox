using System.Windows;
using System.Windows.Media;
using ToolsBox.App.Views;

namespace ToolsBox.App.Infrastructure;

/// <summary>替代默认 MessageBox 的工具箱提示入口，不改变调用方对按钮结果的判断。</summary>
internal static class ComfortMessageBox
{
    public static MessageBoxResult Show(string message, string caption = "宝哥工具箱",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None, ImageSource? artwork = null) =>
        Show(FindOwner(), message, caption, buttons, image, defaultResult, artwork);

    public static MessageBoxResult Show(Window? owner, string message, string caption = "宝哥工具箱",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None, ImageSource? artwork = null)
    {
        var dialog = new ComfortDialogWindow(message, caption, buttons, image, defaultResult, owner) { Artwork = artwork };
        dialog.ShowDialog();
        return dialog.Result;
    }

    private static Window? FindOwner() => Application.Current?.Windows.OfType<Window>()
        .FirstOrDefault(window => window.IsVisible && window.IsActive);
}
