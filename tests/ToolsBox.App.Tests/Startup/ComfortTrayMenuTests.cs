using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace ToolsBox.App.Tests.Startup;

public sealed class ComfortTrayMenuTests
{
    [Fact]
    public Task OpeningMenu_UsesCurrentThemeRatherThanAnOutdatedSavedPreference() => WpfTestThread.RunAsync(() =>
    {
        Type type = typeof(App).Assembly.GetType("ToolsBox.App.Infrastructure.TrayIcon")!;
        var constructor = type.GetConstructor([typeof(Action), typeof(Action), typeof(bool), typeof(Func<bool>)]);
        Assert.NotNull(constructor);
        bool dark = false;
        using var tray = (IDisposable)constructor!.Invoke([new Action(() => { }), new Action(() => { }), false, new Func<bool>(() => dark)]);
        var menu = (ContextMenuStrip)type.GetField("_menu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tray)!;
        dark = true;
        typeof(ToolStripDropDown).GetMethod("OnOpening", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(menu, [new System.ComponentModel.CancelEventArgs()]);
        Assert.Equal(Color.FromArgb(32, 38, 50), menu.BackColor);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false, 255, 255, 255, 24, 37, 54)]
    [InlineData(true, 32, 38, 50, 236, 240, 246)]
    public Task TrayMenu_UsesThemeColorsWithoutChangingActions(bool dark,
        int red, int green, int blue, int textRed, int textGreen, int textBlue) => WpfTestThread.RunAsync(() =>
    {
        Type? type = typeof(App).Assembly.GetType("ToolsBox.App.Infrastructure.ComfortTrayMenu");
        Assert.NotNull(type);
        int opened = 0, exited = 0;
        using var menu = new ContextMenuStrip();
        menu.Items.Add("打开宝哥工具箱(&O)", null, (_, _) => opened++);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出整个工具箱(&X)", null, (_, _) => exited++);
        var apply = type!.GetMethod("Apply", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(apply);
        apply!.Invoke(null, [menu, dark]);
        Assert.Equal(Color.FromArgb(red, green, blue).ToArgb(), menu.BackColor.ToArgb());
        Assert.Equal(Color.FromArgb(textRed, textGreen, textBlue).ToArgb(), menu.ForeColor.ToArgb());
        Assert.IsAssignableFrom<ToolStripProfessionalRenderer>(menu.Renderer);
        Assert.Equal(3, menu.Items.Count);
        Assert.Equal(menu.ForeColor, menu.Items[0].ForeColor);
        ((ToolStripMenuItem)menu.Items[0]).PerformClick();
        ((ToolStripMenuItem)menu.Items[2]).PerformClick();
        Assert.Equal(1, opened);
        Assert.Equal(1, exited);
        return Task.CompletedTask;
    });
}
