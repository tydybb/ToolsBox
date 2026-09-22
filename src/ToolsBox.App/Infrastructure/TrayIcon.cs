using System.Drawing;
using System.Windows;
using System.Windows.Resources;

namespace ToolsBox.App.Infrastructure;

/// <summary>
/// 系统托盘图标：左键单击恢复主窗口，右键菜单提供退出——退出工具箱的唯一入口。
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.ContextMenuStrip _menu;

    public TrayIcon(Action restore, Action exit)
    {
        ArgumentNullException.ThrowIfNull(restore);
        ArgumentNullException.ThrowIfNull(exit);
        _menu = new System.Windows.Forms.ContextMenuStrip();
        _menu.Items.Add("打开宝哥工具箱(&O)", null, (_, _) => restore());
        _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _menu.Items.Add("退出(&X)", null, (_, _) => exit());
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "宝哥工具箱",
            Icon = LoadIcon(),
            ContextMenuStrip = _menu,
            Visible = true,
        };
        // 左键单击（微信习惯）恢复窗口；右键交给上面的菜单。
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left) restore();
        };
    }

    /// <summary>显示一条“已最小化到托盘”气泡；提示失败不影响隐藏或退出流程。</summary>
    public void ShowBalloon(string title, string message)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(3000, title, message, System.Windows.Forms.ToolTipIcon.Info);
        }
        catch (Exception) { /* 提示是可选增强，绝不因它中断窗口隐藏。 */ }
    }

    public void Dispose()
    {
        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        catch (ObjectDisposedException) { }
        _menu.Dispose();
    }

    private static Icon LoadIcon()
    {
        // 与窗口/任务栏同款图标：优先打包资源；测试宿主等非入口程序集没有该资源时回退到进程图标。
        try
        {
            StreamResourceInfo? resource = Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/baoge-toolbox.ico", UriKind.Absolute));
            if (resource?.Stream != null) return new Icon(resource.Stream);
        }
        catch (Exception) { /* 资源装配不匹配时继续回退。 */ }
        try
        {
            string? executable = Environment.ProcessPath;
            if (executable != null && Icon.ExtractAssociatedIcon(executable) is { } icon) return icon;
        }
        catch (Exception) { }
        // 系统共享图标不能被 NotifyIcon 释放，克隆一份交给它。
        return (Icon)SystemIcons.Application.Clone();
    }
}
