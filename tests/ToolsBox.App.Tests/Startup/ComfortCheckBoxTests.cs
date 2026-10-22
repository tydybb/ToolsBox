using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ToolsBox.App.Attendance;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace ToolsBox.App.Tests.Startup;

public sealed class ComfortCheckBoxTests
{
    [Theory]
    [InlineData(false, 96)]
    [InlineData(true, 96)]
    [InlineData(false, 144)]
    [InlineData(true, 144)]
    public Task AttendanceCheckbox_CheckedMarkContrastsAndRendersInBothThemes(bool dark, int dpi) => WpfTestThread.RunAsync(async () =>
    {
        string root = System.IO.Path.Combine(AppContext.BaseDirectory, "checkbox-attendance-" + Guid.NewGuid().ToString("N"));
        var panel = new AttendancePanel(new AttendanceStore(root), _ => { }, () => { }, () => false);
        using var host = new Host(panel);
        try
        {
            ApplyTheme(host.Window, dark);
            await host.LayoutAsync();
            // Stop the panel's poller: this test changes only the visual state, without enabling attendance.
            panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            var box = Assert.IsType<CheckBox>(panel.FindName("AttendanceEnabled"));
            Assert.Same(host.Window.TryFindResource(typeof(CheckBox)), box.Style);
            Assert.Equal(DependencyProperty.UnsetValue, box.ReadLocalValue(FrameworkElement.StyleProperty));
            Assert.Equal(DependencyProperty.UnsetValue, box.ReadLocalValue(Control.TemplateProperty));
            var uncheckedImage = Render(box, dpi);
            box.IsChecked = true;
            await host.LayoutAsync();
            Save(Render(panel, dpi, host.Window.Background), $"comfort-attendance-checkbox-{(dark ? "dark" : "light")}-{dpi}.png");
            AssertVisibleMark(box, host.Window, dpi);
            Assert.True(ChangedPixels(uncheckedImage, Render(box, dpi)) >= 8, "Checking attendance did not visibly change the checkbox indicator.");
        }
        finally
        {
            panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    });

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    public Task CheckedDisabledAndIndeterminateMarksFollowLiveThemeChanges(int dpi) => WpfTestThread.RunAsync(async () =>
    {
        var checkedBox = new CheckBox { Content = "已勾选", IsChecked = true };
        var disabledBox = new CheckBox { Content = "已勾选（禁用）", IsChecked = true, IsEnabled = false };
        var mixedBox = new CheckBox { Content = "部分选择", IsThreeState = true, IsChecked = null };
        var uncheckedBox = new CheckBox { Content = "未勾选", IsChecked = false };
        var panel = new StackPanel { Margin = new Thickness(20) };
        foreach (var box in new[] { checkedBox, disabledBox, mixedBox, uncheckedBox })
        {
            box.Margin = new Thickness(0, 8, 0, 8);
            panel.Children.Add(box);
        }
        using var host = new Host(panel);
        foreach (bool dark in new[] { false, true, false })
        {
            ApplyTheme(host.Window, dark);
            await host.LayoutAsync();
            Save(Render(panel, dpi, host.Window.Background), $"comfort-checkbox-states-{(dark ? "dark" : "light")}-{dpi}.png");
            AssertVisibleMark(checkedBox, host.Window, dpi);
            AssertVisibleMark(disabledBox, host.Window, dpi);
            AssertVisibleMark(mixedBox, host.Window, dpi);
            Assert.Empty(VisibleMarks(uncheckedBox));
            Assert.True(checkedBox.IsChecked);
            Assert.True(disabledBox.IsChecked);
            Assert.Null(mixedBox.IsChecked);
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task HoverKeepsCheckedTickReadableAndKeyboardPreservesToggleAndDisabledBehavior(bool dark) => WpfTestThread.RunAsync(async () =>
    {
        var box = new CheckBox { Content = "_提醒", Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
        int clicks = 0;
        box.Click += (_, _) => clicks++;
        using var host = new Host(new Border { Padding = new Thickness(20), Child = box });
        ApplyTheme(host.Window, dark);
        await host.LayoutAsync();
        // A hit anywhere in the content row must still reach the native CheckBox control.
        for (double x = 4; x < box.ActualWidth; x += 30)
        {
            var hit = Assert.IsAssignableFrom<DependencyObject>(box.InputHitTest(new Point(x, box.ActualHeight / 2)));
            Assert.True(IsWithin(hit, box), $"Checkbox content at x={x} is no longer clickable.");
        }
        Assert.Same(box, Keyboard.Focus(box));
        RaiseKey(box, Keyboard.KeyDownEvent, Key.Space);
        RaiseKey(box, Keyboard.KeyUpEvent, Key.Space);
        await host.LayoutAsync();
        Assert.True(box.IsChecked);
        Assert.Equal(1, clicks);
        SetMouseOver(box, true);
        try
        {
            host.Window.UpdateLayout();
            Assert.True(box.IsMouseOver);
            AssertVisibleMark(box, host.Window, 96);
        }
        finally { SetMouseOver(box, false); }
        RaiseKey(box, Keyboard.KeyDownEvent, Key.Space);
        RaiseKey(box, Keyboard.KeyUpEvent, Key.Space);
        await host.LayoutAsync();
        Assert.False(box.IsChecked);
        Assert.Equal(2, clicks);
        Assert.Contains(Descendants<AccessText>(box), text => text.Text == "_提醒");
        Assert.True(box.Focusable);
        Assert.True(box.IsKeyboardFocused);
        var peer = new CheckBoxAutomationPeer(box);
        var toggle = Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle));
        box.IsEnabled = false;
        Assert.Throws<ElementNotEnabledException>(() => toggle.Toggle());
        Assert.False(box.IsChecked);
        Assert.Equal(2, clicks);
    });

    private static void AssertVisibleMark(CheckBox box, Window window, int dpi)
    {
        var mark = Assert.Single(VisibleMarks(box));
        Brush paint = mark.Stroke ?? mark.Fill;
        var foreground = Assert.IsType<SolidColorBrush>(paint).Color;
        Color background = ((SolidColorBrush)window.Resources["ToolboxSurfaceBrush"]).Color;
        for (DependencyObject? current = VisualTreeHelper.GetParent(mark); current is not null && !ReferenceEquals(current, box); current = VisualTreeHelper.GetParent(current))
            if (current is Border { Background: SolidColorBrush solid } && solid.Color.A == 255) { background = solid.Color; break; }
        double contrast = Contrast(foreground, background);
        Assert.True(contrast >= 3, $"Checkbox mark contrast is {contrast:0.00}: {foreground} on {background}.");
        var pixels = Pixels(Render(mark, dpi));
        int painted = pixels.Where((_, index) => index % 4 == 3).Count(alpha => alpha >= 128);
        Assert.True(painted >= 8, $"Checkbox {mark.Name} {mark.RenderSize} at {VisualTreeHelper.GetOffset(mark)} has {painted} painted pixels (max alpha {pixels.Max()}).");
    }

    private static IEnumerable<Shape> VisibleMarks(CheckBox box) => Descendants<Shape>(box)
        .Where(shape => shape is System.Windows.Shapes.Path or Rectangle && shape.IsVisible && shape.Opacity > 0 && shape.ActualWidth > 0 && shape.ActualHeight > 0 && (shape.Stroke ?? shape.Fill) is SolidColorBrush { Color.A: > 0 });

    private static double Contrast(Color left, Color right)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte value) { double c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        double a = Luminance(left), b = Luminance(right);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    private static RenderTargetBitmap Render(UIElement element, int dpi, Brush? background = null)
    {
        double scale = dpi / 96d;
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(element.RenderSize.Width * scale)), Math.Max(1, (int)Math.Ceiling(element.RenderSize.Height * scale)), dpi, dpi, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        Vector offset = VisualTreeHelper.GetOffset(element);
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(background ?? Brushes.Transparent, null, new Rect(element.RenderSize));
            drawing.DrawRectangle(new VisualBrush(element)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(new Point(offset.X, offset.Y), element.RenderSize),
                Stretch = Stretch.Fill
            }, null, new Rect(element.RenderSize));
        }
        bitmap.Render(visual);
        return bitmap;
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    private static int ChangedPixels(BitmapSource before, BitmapSource after)
    {
        Assert.Equal(before.PixelWidth, after.PixelWidth);
        Assert.Equal(before.PixelHeight, after.PixelHeight);
        byte[] left = Pixels(before), right = Pixels(after);
        return Enumerable.Range(0, left.Length / 4).Count(pixel => Enumerable.Range(0, 4).Any(channel => left[pixel * 4 + channel] != right[pixel * 4 + channel]));
    }

    private static void Save(BitmapSource bitmap, string name)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, name));
        encoder.Save(stream);
    }

    private static void RaiseKey(CheckBox box, RoutedEvent routedEvent, Key key) => box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box)!, Environment.TickCount, key) { RoutedEvent = routedEvent });

    private static void SetMouseOver(CheckBox box, bool value)
    {
        // Use WPF's own mouse-over propagation so its cached flags and template triggers agree.
        typeof(MouseDevice).GetMethod("ChangeMouseOver", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Mouse.PrimaryDevice, [value ? box : null, Environment.TickCount]);
    }

    private static bool IsWithin(DependencyObject child, DependencyObject parent)
    {
        for (DependencyObject? current = child; current is not null; current = current is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, parent)) return true;
        return false;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void ApplyTheme(Window window, bool dark)
    {
        var type = typeof(MainWindow).Assembly.GetType("ToolsBox.App.Infrastructure.ComfortAppearance", true)!;
        type.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [window, dark]);
        window.Background = (Brush)window.Resources["ToolboxSurfaceBrush"];
    }

    private sealed class Host : IDisposable
    {
        public Host(FrameworkElement content)
        {
            Window = new Window { Content = content, Width = 700, Height = 360, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
            Window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/宝哥工具箱;component/Themes/ComfortControls.xaml", UriKind.Relative) });
            Window.Show();
            Assert.NotEqual(IntPtr.Zero, new WindowInteropHelper(Window).Handle);
        }
        public Window Window { get; }
        public async Task LayoutAsync()
        {
            Window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Window.UpdateLayout();
        }
        public void Dispose() => Window.Close();
    }
}
