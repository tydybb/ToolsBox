using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ToolsBox.App.NetworkTraffic;
using ToolsBox.App.Views;
using ToolsBox.Core.FileUnlocking;
using ToolsBox.Core.NetworkTraffic;
using Xunit.Abstractions;

namespace ToolsBox.App.Tests.Startup;

public sealed class ComfortWindowClientProbeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task BandwidthActions_FitTheNativeClientAtItsDeclaredFixedSize(bool dark) => WpfTestThread.RunAsync(() =>
    {
        var window = new BandwidthLimitWindow(new ApplicationTrafficItemViewModel(
            new ApplicationTrafficSnapshot("demo", "演示程序", "C:\\Example.exe", true, 0, 0, 0, 0, []), null));
        try
        {
            Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
            Assert.Equal(540, window.Width);
            Assert.Equal(380, window.Height);
            ProbeClient(window, dark, "bandwidth-fixed", 7);
            var value = Assert.IsType<TextBox>(window.FindName("ValueTextBox"));
            AssertReadableInput(value, "Bandwidth value");
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PortSearchInput_KeepsItsGlyphInsideTheIconTemplateViewport(bool dark) => WpfTestThread.RunAsync(() =>
    {
        var view = new PortMonitorView();
        var host = new Window
        {
            Width = 900, Height = 560, Content = view, WindowStyle = WindowStyle.None,
            ShowInTaskbar = false
        };
        Initialize(host);
        try
        {
            var search = Assert.IsType<TextBox>(view.FindName("PortSearchInput"));
            search.Text = "8080";
            ProbeClient(host, dark, "port-search-input", 0);
            AssertReadableInput(search, "Port search input");
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FixedAndAutomaticTextInputs_KeepTheirCharactersInsideTheNativeViewport(bool dark) => WpfTestThread.RunAsync(() =>
    {
        var fixedInput = new TextBox { Text = "08:30", Height = 38, Margin = new Thickness(0, 0, 0, 18) };
        var automaticInput = new TextBox { Text = "完整输入 09:25" };
        var inputs = new StackPanel();
        inputs.Children.Add(fixedInput);
        inputs.Children.Add(automaticInput);
        var host = new Window
        {
            Width = 400, Height = 220, Content = new Border { Padding = new Thickness(24), Child = inputs },
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false
        };
        Initialize(host);
        try
        {
            ProbeClient(host, dark, "text-inputs", 0);
            Assert.Equal(38, fixedInput.ActualHeight);
            AssertReadableInput(fixedInput, "Fixed 38-DIP input");
            AssertReadableInput(automaticInput, "Automatic input");
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task ProcessActions_FitTheNativeClientAtDefaultAndMinimumSize(bool dark, bool minimum) => WpfTestThread.RunAsync(() =>
    {
        var window = new ProcessTerminationDialog("C:\\演示文件夹\\项目数据", [
            new FileLockEntry("example", 1, 123, "演示程序", "C:\\Example.exe", null, false),
            new FileLockEntry("example2", 2, 456, "文件查看程序", "C:\\Viewer.exe", null, false)]);
        try
        {
            Assert.Equal(650, window.MinWidth);
            Assert.Equal(480, window.MinHeight);
            if (minimum) { window.Width = window.MinWidth; window.Height = window.MinHeight; }
            ProbeClient(window, dark, $"process-{(minimum ? "minimum" : "default")}", 2);
            var list = Assert.IsType<DataGrid>(window.FindName("ProcessList"));
            output.WriteLine($"Process list render area: {list.ActualWidth:0.##} x {list.ActualHeight:0.##} DIP.");
            Assert.False(Assert.IsType<CheckBox>(window.FindName("RiskAcknowledgement")).IsChecked);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PortSortPopup_UsesTheThemeForGutterHoverAndKeyboardSelection(bool dark) => WpfTestThread.RunAsync(async () =>
    {
        // This isolated view has no VM, poller, network source, or MainWindow Loaded route.
        var view = new PortMonitorView();
        var host = new Window
        {
            Width = 900, Height = 560, Content = view, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, ShowActivated = true, ShowInTaskbar = false, Opacity = 0
        };
        Initialize(host);
        host.Show();
        // Keyboard menu navigation needs an active native owner, even though the isolated host stays transparent.
        host.Activate();
        Apply(host, dark);
        var grid = Assert.IsType<DataGrid>(view.FindName("PortGrid"));
        ContextMenu menu = Assert.IsType<ContextMenu>(grid.ContextMenu);
        int clicks = 0;
        foreach (MenuItem item in menu.Items.OfType<MenuItem>()) item.Click += (_, _) => clicks++;
        try
        {
            Assert.Null(grid.ItemsSource);
            menu.PlacementTarget = grid;
            menu.Placement = PlacementMode.Relative;
            menu.HorizontalOffset = 40;
            menu.VerticalOffset = 24;
            menu.IsOpen = true;
            await LayoutAsync(host, menu);
            Assert.NotNull(PresentationSource.FromVisual(menu));
            MenuItem[] items = menu.Items.OfType<MenuItem>().ToArray();
            Assert.Equal(9, items.Length);
            output.WriteLine($"Port sort popup: {menu.ActualWidth:0.##} x {menu.ActualHeight:0.##} DIP; theme {(dark ? "dark" : "light")}.");

            RenderTargetBitmap normal = SavePopup(menu, dark, "normal");
            Color gutter = At(normal, 16, Center(items[^1], menu).Y);
            output.WriteLine($"Gutter pixel: {gutter}; Surface {Theme(host, "Surface")}; Sidebar {Theme(host, "Sidebar")}.");

            MenuItem hoverItem = items[1];
            ChangeMouseOver(hoverItem);
            // Capture WPF's propagated hover before real cursor processing replaces the isolated synthetic state.
            menu.UpdateLayout();
            Assert.True(hoverItem.IsMouseOver);
            RenderTargetBitmap hover = SavePopup(menu, dark, "hover");
            Color hoverFill = At(hover, menu.ActualWidth - 12, Center(hoverItem, menu).Y);
            output.WriteLine($"Hovered item: highlighted {hoverItem.IsHighlighted}; right fill {hoverFill}; palette selection {Theme(host, "Selection")}.");

            ChangeMouseOver(null);
            Assert.Same(items[2], Keyboard.Focus(items[2]));
            items[2].RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(menu)!, Environment.TickCount, Key.Down)
                { RoutedEvent = Keyboard.KeyDownEvent });
            // Native key navigation is synchronous; do not yield to unrelated desktop focus messages before observing it.
            menu.UpdateLayout();
            var selected = Assert.IsType<MenuItem>(Keyboard.FocusedElement);
            Assert.Same(items[3], selected);
            Assert.True(selected.IsHighlighted);
            RenderTargetBitmap keyboard = SavePopup(menu, dark, "keyboard");
            Color selectedFill = At(keyboard, menu.ActualWidth - 12, Center(selected, menu).Y);
            output.WriteLine($"Keyboard selected item: {selected.Header}; right fill {selectedFill}.");
            Assert.Equal(0, clicks);
            Assert.Null(grid.ItemsSource);

            Assert.Equal(Theme(host, "Surface"), ColorOf(menu.Background));
            Assert.Equal(Theme(host, "Text"), ColorOf(menu.Foreground));
            Assert.True(gutter == Theme(host, "Surface") || gutter == Theme(host, "Sidebar"),
                $"{(dark ? "Dark" : "Light")} popup gutter renders {gutter}, outside the theme Surface/Sidebar palette.");
            Assert.Equal(Theme(host, "Selection"), hoverFill);
            Assert.Equal(Theme(host, "Selection"), selectedFill);
            Assert.All(items, item => Assert.Equal(Theme(host, "Text"), ColorOf(item.Foreground)));
        }
        finally { ChangeMouseOver(null); menu.IsOpen = false; host.Close(); }
    });

    private void ProbeClient(Window window, bool dark, string name, int actionCount)
    {
        // EnsureHandle creates native chrome without Show or Loaded and therefore does not start services.
        IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
        Assert.True(GetClientRect(handle, out NativeRect client));
        var source = Assert.IsType<HwndSource>(HwndSource.FromHwnd(handle));
        Matrix fromDevice = source.CompositionTarget.TransformFromDevice;
        Point logical = fromDevice.Transform(new Point(client.Right - client.Left, client.Bottom - client.Top));
        uint dpi = GetDpiForWindow(handle);
        Apply(window, dark);
        var root = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        root.Measure(new Size(logical.X, logical.Y));
        root.Arrange(new Rect(0, 0, logical.X, logical.Y));
        root.UpdateLayout();
        output.WriteLine($"{window.GetType().Name} {(dark ? "dark" : "light")}: outer {window.Width:0.##} x {window.Height:0.##} DIP; HWND client {client.Right - client.Left} x {client.Bottom - client.Top} px; DPI {dpi}; logical client {logical.X:0.##} x {logical.Y:0.##} DIP.");
        SaveClient(window, logical, 96, $"comfort-native-client-{name}-{(dark ? "dark" : "light")}-96.png");
        // A 144-DPI rendering checks text and brush rasterization; native geometry above remains at the measured monitor DPI.
        SaveClient(window, logical, 144, $"comfort-native-client-{name}-{(dark ? "dark" : "light")}-144.png");
        Button[] buttons = Descendants<Button>(root).Where(button => button.Visibility == Visibility.Visible && button.Content is string).ToArray();
        Assert.Equal(actionCount, buttons.Length);
        Vector rootOffset = VisualTreeHelper.GetOffset(root);
        foreach (Button button in buttons)
        {
            Rect bounds = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
            bounds.Offset(rootOffset);
            output.WriteLine($"Action '{button.Content}': {bounds.X:0.##}, {bounds.Y:0.##}, {bounds.Width:0.##} x {bounds.Height:0.##} DIP.");
            Assert.True(button.ActualHeight >= 38, $"{window.GetType().Name}/{button.Content}: height {button.ActualHeight:0.##} DIP.");
            Assert.True(bounds.Left >= -0.5 && bounds.Top >= -0.5 && bounds.Right <= logical.X + 0.5 && bounds.Bottom <= logical.Y + 0.5,
                $"{window.GetType().Name}/{button.Content} bounds {bounds} exceed the measured client {logical.X:0.##} x {logical.Y:0.##} DIP.");
        }
    }

    private void AssertReadableInput(TextBox value, string name)
    {
        var textHost = Assert.IsType<ScrollViewer>(value.Template.FindName("PART_ContentHost", value));
        Rect character = value.GetRectFromCharacterIndex(0);
        var viewport = Assert.Single(Descendants<ScrollContentPresenter>(textHost));
        Point origin = viewport.TransformToAncestor(value).Transform(new Point());
        var viewportBounds = new Rect(origin, viewport.RenderSize);
        output.WriteLine($"{name}: control {value.ActualWidth:0.##} x {value.ActualHeight:0.##}; host {textHost.ActualWidth:0.##} x {textHost.ActualHeight:0.##}; viewport {viewportBounds}; character {character}.");
        Assert.True(character.Height >= value.FontSize, $"{name}: character geometry was unavailable.");
        Assert.True(character.Top >= viewportBounds.Top - 0.5 && character.Bottom <= viewportBounds.Bottom + 0.5,
            $"{name}: character {character} extends outside the rendered viewport {viewportBounds}.");
    }

    private static void SaveClient(Window window, Point logical, int dpi, string name)
    {
        double scale = dpi / 96d;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(logical.X * scale), (int)Math.Ceiling(logical.Y * scale), dpi, dpi, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (DrawingContext drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, logical.X, logical.Y));
        bitmap.Render(background);
        bitmap.Render((FrameworkElement)window.Content);
        Save(bitmap, name);
    }

    private static RenderTargetBitmap SavePopup(ContextMenu menu, bool dark, string state)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth), (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(menu);
        Save(bitmap, $"comfort-port-sort-popup-{(dark ? "dark" : "light")}-{state}.png");
        return bitmap;
    }

    private static void Save(BitmapSource bitmap, string name)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, name));
        encoder.Save(stream);
    }

    private static Color At(BitmapSource bitmap, double x, double y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect((int)x, (int)y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private static Point Center(MenuItem item, ContextMenu menu) => item.TransformToAncestor(menu).Transform(new Point(item.ActualWidth / 2, item.ActualHeight / 2));
    private static Color Theme(Window window, string name) => ColorOf((Brush)window.Resources["Toolbox" + name + "Brush"]);
    private static Color ColorOf(Brush brush) => Assert.IsType<SolidColorBrush>(brush).Color;
    private static Type Appearance => typeof(App).Assembly.GetType("ToolsBox.App.Infrastructure.ComfortAppearance")!;
    private static void Initialize(Window window) => Appearance.GetMethod("InitializeDialogWindow")!.Invoke(null, [window]);
    private static void Apply(Window window, bool dark) => Appearance.GetMethod("Apply")!.Invoke(null, [window, dark]);
    private static void ChangeMouseOver(MenuItem? item) => typeof(MouseDevice).GetMethod("ChangeMouseOver", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(Mouse.PrimaryDevice, [item, Environment.TickCount]);

    private static async Task LayoutAsync(Window window, ContextMenu menu)
    {
        window.UpdateLayout(); menu.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.UpdateLayout(); menu.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr handle);
}
