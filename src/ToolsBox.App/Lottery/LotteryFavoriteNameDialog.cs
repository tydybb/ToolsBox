using System.Windows;
using System.Windows.Controls;

namespace ToolsBox.App.Lottery;

public sealed class LotteryFavoriteNameDialog : Window
{
    private readonly TextBox _name;
    public string FavoriteName => _name.Text.Trim();

    public LotteryFavoriteNameDialog(string suggestedName)
    {
        Title = "保存手动幸运号码";
        Width = 380; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "给这组手动输入的号码起个名字（1–40 字）", TextWrapping = TextWrapping.Wrap });
        _name = new TextBox { Text = suggestedName, MaxLength = 40, Padding = new Thickness(8), Margin = new Thickness(0,12,0,6) };
        panel.Children.Add(_name);
        var error = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick };
        panel.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
        var cancel = new Button { Content = "取消", IsCancel = true, Padding = new Thickness(16,7,16,7) };
        var save = new Button { Content = "保存", IsDefault = true, Padding = new Thickness(16,7,16,7), Margin = new Thickness(10,0,0,0) };
        save.Click += (_, _) =>
        {
            if (FavoriteName.Length is < 1 or > 40) { error.Text = "请输入 1–40 个字符的名称。"; return; }
            DialogResult = true;
        };
        actions.Children.Add(cancel); actions.Children.Add(save); panel.Children.Add(actions);
        Content = panel;
        Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }

    public static string? Prompt(string suggestedName)
    {
        var dialog = new LotteryFavoriteNameDialog(suggestedName);
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        if (owner is not null) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true ? dialog.FavoriteName : null;
    }
}
