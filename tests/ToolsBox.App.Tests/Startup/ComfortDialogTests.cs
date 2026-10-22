using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ToolsBox.App.Tests.Startup;

public sealed class ComfortDialogTests
{
    [Theory]
    [InlineData(false, 96)]
    [InlineData(true, 96)]
    [InlineData(false, 144)]
    [InlineData(true, 144)]
    public Task Dialog_LoadsOwnControlsAndFollowsOwnerTheme(bool dark, int dpi) => WpfTestThread.RunAsync(async () =>
    {
        using var owner = new Host(new Window());
        ApplyTheme(owner.Window, dark);
        var dialog = Create("你早退了，距离正常下班还差 507 分钟。\n明天调休补班，今晚安心休息。\n\n实际下班：2026-10-09 08:47:32", "下班提醒", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.None, owner.Window);
        using var host = new Host(dialog);
        await LayoutAsync(dialog);
        Assert.Equal(dark, dialog.Resources["ToolboxIsDarkTheme"]);
        Assert.IsType<Style>(dialog.TryFindResource("ToolboxButtonStyle"));
        Assert.IsType<Style>(dialog.TryFindResource(typeof(ScrollBar)));
        Assert.Equal(((SolidColorBrush)dialog.Resources["ToolboxBackgroundBrush"]).Color, ((SolidColorBrush)dialog.Background).Color);
        var message = Assert.IsType<TextBlock>(dialog.FindName("DialogMessage"));
        Assert.Equal(TextWrapping.Wrap, message.TextWrapping);
        Assert.Contains("明天调休补班", message.Text);
        Assert.Equal(((SolidColorBrush)dialog.Resources["ToolboxTextBrush"]).Color, ((SolidColorBrush)message.Foreground).Color);
        Assert.Single(Buttons(dialog));
        Save(Render(Assert.IsAssignableFrom<FrameworkElement>(dialog.Content), dpi), $"comfort-dialog-{(dark ? "dark" : "light")}-{dpi}.png");
    });

    [Theory]
    [InlineData(MessageBoxButton.OK, MessageBoxResult.OK, "OK")]
    [InlineData(MessageBoxButton.OKCancel, MessageBoxResult.Cancel, "OK,Cancel")]
    [InlineData(MessageBoxButton.YesNo, MessageBoxResult.No, "Yes,No")]
    [InlineData(MessageBoxButton.YesNoCancel, MessageBoxResult.Cancel, "Yes,No,Cancel")]
    public Task Buttons_MapToRealResultsAndDefaultCloseIsSafe(MessageBoxButton buttons, MessageBoxResult expectedClose, string resultNames) => WpfTestThread.RunAsync(async () =>
    {
        var dialog = Create("是否继续？", "操作确认", buttons);
        using var host = new Host(dialog);
        await LayoutAsync(dialog);
        Assert.Equal(resultNames, string.Join(",", Buttons(dialog).Select(button => button.Tag!.ToString())));
        var defaultButton = Assert.Single(Buttons(dialog).Where(button => button.IsDefault));
        Assert.Equal(expectedClose, defaultButton.Tag);
        Assert.True(defaultButton.IsKeyboardFocused);
        dialog.Close();
        Assert.Equal(expectedClose, Result(dialog));
    });

    [Theory]
    [InlineData(MessageBoxButton.OKCancel, MessageBoxResult.Cancel)]
    [InlineData(MessageBoxButton.YesNo, MessageBoxResult.No)]
    [InlineData(MessageBoxButton.YesNoCancel, MessageBoxResult.Cancel)]
    public Task Escape_DoesNotAcceptConfirmationEvenWithPositiveDefault(MessageBoxButton buttons, MessageBoxResult expected) => WpfTestThread.RunAsync(async () =>
    {
        MessageBoxResult positive = buttons == MessageBoxButton.OKCancel ? MessageBoxResult.OK : MessageBoxResult.Yes;
        var dialog = Create("高风险操作，继续？", "确认", buttons, MessageBoxImage.Warning, positive);
        using var host = new Host(dialog);
        await LayoutAsync(dialog);
        Assert.Equal(positive, Assert.Single(Buttons(dialog).Where(button => button.IsDefault)).Tag);
        dialog.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog)!, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.Equal(expected, Result(dialog));
        Assert.False(dialog.IsVisible);
    });

    [Theory]
    [InlineData(MessageBoxButton.OK, MessageBoxResult.OK)]
    [InlineData(MessageBoxButton.OKCancel, MessageBoxResult.Cancel)]
    [InlineData(MessageBoxButton.YesNo, MessageBoxResult.No)]
    [InlineData(MessageBoxButton.YesNoCancel, MessageBoxResult.Cancel)]
    public Task Enter_ActivatesTheFocusedSafeDefault(MessageBoxButton buttons, MessageBoxResult expected) => WpfTestThread.RunAsync(async () =>
    {
        var dialog = Create("是否继续？", "操作确认", buttons);
        using var host = new Host(dialog);
        await LayoutAsync(dialog);
        var button = Assert.Single(Buttons(dialog).Where(candidate => candidate.IsDefault));
        Assert.True(button.IsKeyboardFocused);
        button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(button)!, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
        Assert.Equal(expected, Result(dialog));
        Assert.False(dialog.IsVisible);
    });

    [Theory]
    [InlineData(MessageBoxButton.OK, MessageBoxResult.OK)]
    [InlineData(MessageBoxButton.OKCancel, MessageBoxResult.OK)]
    [InlineData(MessageBoxButton.OKCancel, MessageBoxResult.Cancel)]
    [InlineData(MessageBoxButton.YesNo, MessageBoxResult.Yes)]
    [InlineData(MessageBoxButton.YesNo, MessageBoxResult.No)]
    [InlineData(MessageBoxButton.YesNoCancel, MessageBoxResult.Yes)]
    [InlineData(MessageBoxButton.YesNoCancel, MessageBoxResult.No)]
    [InlineData(MessageBoxButton.YesNoCancel, MessageBoxResult.Cancel)]
    public Task ClickingEachButton_ReturnsItsSpecificResult(MessageBoxButton buttons, MessageBoxResult choice) => WpfTestThread.RunAsync(async () =>
    {
        var dialog = Create("确认内容", "操作确认", buttons);
        using var host = new Host(dialog);
        await LayoutAsync(dialog);
        Assert.Single(Buttons(dialog).Where(button => Equals(button.Tag, choice))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(choice, Result(dialog));
        Assert.False(dialog.IsVisible);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LongMessageAndOptionalArtwork_StayInsideBoundedDialog(bool dark) => WpfTestThread.RunAsync(async () =>
    {
        using var owner = new Host(new Window());
        ApplyTheme(owner.Window, dark);
        var dialog = Create(string.Join("\n", Enumerable.Range(1, 120).Select(index => $"第 {index} 个进程：这是需要完整可读的操作结果。")), "操作结果", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.None, owner.Window);
        var artwork = new DrawingImage(new GeometryDrawing(Brushes.Orange, new Pen(Brushes.Black, 2), new EllipseGeometry(new Point(50, 50), 45, 45)));
        dialog.GetType().GetProperty("Artwork")!.SetValue(dialog, artwork);
        using var host = new Host(dialog);
        await LayoutAsync(dialog);
        var scroll = Assert.IsType<ScrollViewer>(dialog.FindName("MessageScrollViewer"));
        Assert.True(scroll.ScrollableHeight > 0);
        Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
        Assert.True(dialog.ActualHeight <= dialog.MaxHeight + 1);
        Assert.True(dialog.ActualWidth <= dialog.MaxWidth + 1);
        Assert.True(Assert.Single(Buttons(dialog)).IsVisible);
        Assert.Same(artwork, Assert.IsType<Image>(dialog.FindName("DialogArtwork")).Source);
        Assert.True(Assert.IsType<Image>(dialog.FindName("DialogArtwork")).IsVisible);
        Assert.True(IsInside(scroll, dialog));
        Assert.True(IsInside(Assert.Single(Buttons(dialog)), dialog));
        scroll.ScrollToBottom();
        await LayoutAsync(dialog);
        Assert.Equal(scroll.ScrollableHeight, scroll.VerticalOffset, 1);
        Save(Render(Assert.IsAssignableFrom<FrameworkElement>(dialog.Content), 144), $"comfort-dialog-long-{(dark ? "dark" : "light")}.png");
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public Task ShortWorkArea_KeepsHeadingMessageAndEveryButtonInside(bool dark, bool withArtwork) => WpfTestThread.RunAsync(async () =>
    {
        using var owner = new Host(new Window());
        ApplyTheme(owner.Window, dark);
        var dialog = Create(string.Join("\n", Enumerable.Repeat("操作需要确认：长内容可以滚动查看，不能遮住取消按钮。", 40)), "操作确认", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.None, owner.Window);
        if (withArtwork)
            dialog.GetType().GetProperty("Artwork")!.SetValue(dialog, new DrawingImage(new GeometryDrawing(Brushes.Orange, null, new EllipseGeometry(new Point(40, 40), 40, 40))));
        // Deterministic DIP work-area probe; no real monitor configuration or resolution change.
        using var host = new Host(dialog);
        dialog.GetType().GetMethod("ApplyWorkAreaBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [new Rect(0, 0, 480, 300)]);
        await LayoutAsync(dialog);
        Assert.True(dialog.ActualHeight <= 252 + 1);
        Assert.True(dialog.ActualWidth <= 448 + 1);
        var message = Assert.IsType<ScrollViewer>(dialog.FindName("MessageScrollViewer"));
        Assert.True(message.ActualHeight >= 24, "At least one readable line must remain in the scrollable message viewport.");
        Assert.True(message.ActualWidth >= 140, "The optional artwork must not squeeze the message out of the window.");
        Assert.True(IsInside(Assert.IsType<TextBlock>(dialog.FindName("DialogHeading")), dialog));
        Assert.True(IsInside(message, dialog));
        foreach (Button button in Buttons(dialog)) Assert.True(IsInside(button, dialog), $"Button {button.Tag} was clipped in the short work area.");
        if (withArtwork) Assert.True(IsInside(Assert.IsType<Image>(dialog.FindName("DialogArtwork")), dialog));
        Save(Render(Assert.IsAssignableFrom<FrameworkElement>(dialog.Content), 144), $"comfort-dialog-compact-{(dark ? "dark" : "light")}.png");
    });

    [Fact]
    public Task Service_WithOwnModalHostReturnsChosenResultWithoutDefaultMessageBox() => WpfTestThread.RunAsync(async () =>
    {
        using var owner = new Host(new Window());
        var service = typeof(MainWindow).Assembly.GetType("ToolsBox.App.Infrastructure.ComfortMessageBox");
        Assert.NotNull(service);
        var show = service.GetMethod("Show", BindingFlags.Public | BindingFlags.Static, null,
            [typeof(Window), typeof(string), typeof(string), typeof(MessageBoxButton), typeof(MessageBoxImage), typeof(MessageBoxResult), typeof(ImageSource)], null);
        Assert.NotNull(show);
        _ = owner.Window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var dialog = Assert.Single(owner.Window.OwnedWindows.Cast<Window>());
            dialog.Opacity = 0;
            Assert.Single(Buttons(dialog).Where(button => Equals(button.Tag, MessageBoxResult.No))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        var result = show.Invoke(null, [owner.Window, "取消下载任务？", "操作确认", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.None, null]);
        Assert.Equal(MessageBoxResult.No, result);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Empty(owner.Window.OwnedWindows.Cast<Window>());
    });

    private static Window Create(string message, string caption, MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None, Window? owner = null)
    {
        var type = typeof(MainWindow).Assembly.GetType("ToolsBox.App.Views.ComfortDialogWindow");
        Assert.NotNull(type);
        return Assert.IsAssignableFrom<Window>(Activator.CreateInstance(type, message, caption, buttons, image, defaultResult, owner));
    }

    private static MessageBoxResult Result(Window dialog) => Assert.IsType<MessageBoxResult>(dialog.GetType().GetProperty("Result")!.GetValue(dialog));
    private static IEnumerable<Button> Buttons(Window dialog) => Assert.IsType<WrapPanel>(dialog.FindName("ResultButtons")).Children.OfType<Button>();
    private static bool IsInside(FrameworkElement child, Window window)
    {
        // Window.ActualHeight includes its native caption; descendants can fit that box but still
        // be clipped by the smaller client area. Compare with the actual content viewport instead.
        var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        var bounds = child.TransformToAncestor(content).TransformBounds(new Rect(child.RenderSize));
        return bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= content.ActualWidth + 1 && bounds.Bottom <= content.ActualHeight + 1;
    }
    private static async Task LayoutAsync(Window window) { window.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
    private static void ApplyTheme(Window window, bool dark) => typeof(MainWindow).Assembly.GetType("ToolsBox.App.Infrastructure.ComfortAppearance", true)!.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [window, dark]);
    private static RenderTargetBitmap Render(FrameworkElement element, int dpi)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpi / 96d), (int)Math.Ceiling(element.ActualHeight * dpi / 96d), dpi, dpi, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        Vector offset = VisualTreeHelper.GetOffset(element);
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(element) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(new Point(offset.X, offset.Y), element.RenderSize), Stretch = Stretch.Fill }, null, new Rect(element.RenderSize));
        bitmap.Render(visual);
        return bitmap;
    }
    private static void Save(BitmapSource bitmap, string filename)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, filename)); encoder.Save(file);
    }
    private sealed class Host : IDisposable
    {
        public Host(Window window)
        {
            Window = window; window.ShowActivated = false; window.ShowInTaskbar = false; window.Opacity = 0;
            if (double.IsNaN(window.Width)) window.Width = 500;
            if (double.IsNaN(window.Height) && window.SizeToContent == SizeToContent.Manual) window.Height = 400;
            window.Show();
            Assert.NotEqual(IntPtr.Zero, new WindowInteropHelper(window).Handle);
        }
        public Window Window { get; }
        public void Dispose() { if (Window.IsLoaded) Window.Close(); }
    }
}
