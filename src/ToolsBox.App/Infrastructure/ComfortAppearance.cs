using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ToolsBox.App.Infrastructure;

/// <summary>按窗口应用统一主题；只改变浏览工具外壳，不改变网页内容。</summary>
internal static class ComfortAppearance
{
    private static readonly ConditionalWeakTable<Window, object> DialogWindows = new();
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolsBox", "appearance.json");

    /// <summary>独立进程及构造后才指定 Owner 的窗口，也加载完整控件样式并跟随所属窗口主题。</summary>
    public static void InitializeDialogWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (DialogWindows.TryGetValue(window, out _)) return;
        DialogWindows.Add(window, new object());
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"/{typeof(ComfortAppearance).Assembly.GetName().Name};component/Themes/ComfortControls.xaml", UriKind.Relative)
        });
        window.SetResourceReference(Window.BackgroundProperty, "ToolboxBackgroundBrush");
        window.SetResourceReference(Window.ForegroundProperty, "ToolboxTextBrush");
        window.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei");
        window.FontSize = 13;
        window.UseLayoutRounding = true;
        ApplyDialogTheme(window);
        window.SourceInitialized += (_, _) => ApplyDialogTheme(window);
        window.Activated += (_, _) =>
        {
            // 浏览器和打卡代理运行在独立普通权限进程，重新激活时读取主工具箱已保存的外观偏好。
            if (window.Owner is null) ApplyDialogTheme(window);
        };
        window.Closed += (_, _) => DialogWindows.Remove(window);
    }

    private static void ApplyDialogTheme(Window window) =>
        Apply(window, window.Owner?.Resources["ToolboxIsDarkTheme"] is bool dark ? dark : LoadDarkPreference());

    public static bool LoadDarkPreference()
    {
        try
        {
            if (new FileInfo(SettingsPath) is not { Exists: true, Length: <= 4096 }) return false;
            return JsonSerializer.Deserialize<Preference>(File.ReadAllText(SettingsPath))?.Dark ?? false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    public static void SaveDarkPreference(bool dark)
    {
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Preference(dark)));
            File.Move(temporary, SettingsPath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 本次外观仍有效。 */ }
        finally
        {
            try { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public static void Apply(Window window, bool dark)
    {
        window.Resources["ToolboxIsDarkTheme"] = dark;
        var colors = new Dictionary<string, string>
        {
            ["Background"] = dark ? "#141820" : "#F5F7FB",
            ["Surface"] = dark ? "#202632" : "#FFFFFF",
            ["Sidebar"] = dark ? "#191E28" : "#EDF1F7",
            ["Text"] = dark ? "#ECF0F6" : "#182536",
            ["Muted"] = dark ? "#A6B2C3" : "#66758A",
            ["Border"] = dark ? "#333D4D" : "#E2E8F0",
            ["Hover"] = dark ? "#293342" : "#F3F6FC",
            ["Selection"] = dark ? "#263E5C" : "#EAF2FF",
            ["Accent"] = dark ? "#7DB5FF" : "#2369DA",
            ["AccentFill"] = dark ? "#377CE3" : "#2369DA",
            ["AccentText"] = "#FFFFFF",
            ["InfoSurface"] = dark ? "#263E5C" : "#EAF2FF",
            ["InfoText"] = dark ? "#A4CAFF" : "#315A8A",
            ["SuccessSurface"] = dark ? "#203C32" : "#E9F7EF",
            ["SuccessText"] = dark ? "#72D5A4" : "#167448",
            ["WarningSurface"] = dark ? "#463921" : "#FFF5DF",
            ["WarningText"] = dark ? "#EDC174" : "#996719",
            ["DangerSurface"] = dark ? "#462A32" : "#FFF0F0",
            ["DangerText"] = dark ? "#FFADB5" : "#B42336"
        };
        foreach (var (name, color) in colors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            window.Resources["Toolbox" + name + "Brush"] = brush;
        }
        // 原生滚动条、复选框及默认单元格也使用与当前主题一致的系统画刷。
        window.Resources[SystemColors.WindowBrushKey] = window.Resources["ToolboxSurfaceBrush"];
        window.Resources[SystemColors.WindowTextBrushKey] = window.Resources["ToolboxTextBrush"];
        window.Resources[SystemColors.ControlBrushKey] = window.Resources["ToolboxSidebarBrush"];
        window.Resources[SystemColors.ControlTextBrushKey] = window.Resources["ToolboxTextBrush"];
        window.Resources[SystemColors.HighlightBrushKey] = window.Resources["ToolboxSelectionBrush"];
        window.Resources[SystemColors.HighlightTextBrushKey] = window.Resources["ToolboxTextBrush"];
        window.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = window.Resources["ToolboxSelectionBrush"];
        window.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = window.Resources["ToolboxTextBrush"];
        UpdateCaption(window, dark);
        // Owner 并非 WPF 资源父级：直接更新已打开的所属窗口，不只更新主窗口自身。
        foreach (var dialog in DialogWindows)
            if (dialog.Key.Dispatcher.CheckAccess() && ReferenceEquals(dialog.Key.Owner, window)) Apply(dialog.Key, dark);
    }

    public static void UpdateCaption(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        try
        {
            int enabled = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
            int caption = dark ? 0x00281E19 : 0x00F7F1ED;
            int text = dark ? 0x00F6F0EC : 0x00362518;
            _ = DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
            _ = DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private sealed record Preference(bool Dark);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
