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
using System.Xml.Linq;

namespace ToolsBox.App.Tests.Startup;

public sealed class ComfortScrollBarTests
{
    [Fact]
    public void ApplicationResources_IncludeSharedControlsAndScrollbarsForStandaloneWindows()
    {
        var document = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "ToolsBox.App", "App.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        string[] sources = document.Descendants(ns + "ResourceDictionary")
            .Select(element => (string?)element.Attribute("Source")).OfType<string>().ToArray();
        Assert.Contains("Themes/ComfortScrollBars.xaml", sources);
        Assert.Contains("Themes/ComfortControls.xaml", sources);
    }

    [Fact]
    public Task StandaloneDictionary_ResolvesItsImplicitStyleAndFallbackPalette() => WpfTestThread.RunAsync(async () =>
    {
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "src", "ToolsBox.App", "Themes", "ComfortScrollBars.xaml")),
            "The standalone scrollbar dictionary is missing.");
        var bar = new ScrollBar { Maximum = 100, ViewportSize = 10 };
        using var host = new Host(bar, "ComfortScrollBars.xaml");
        await host.LayoutAsync();
        AssertComfortBar(bar);
        var shape = ThumbShape(bar);
        Assert.Equal(Color.FromRgb(0x66, 0x75, 0x8A), ((SolidColorBrush)shape.Background).Color);
    });

    [Theory]
    [InlineData(Orientation.Vertical)]
    [InlineData(Orientation.Horizontal)]
    public Task NativeScrollbar_UsesNarrowRoundedThumbAndCorrectTrackDirection(Orientation orientation) => WpfTestThread.RunAsync(async () =>
    {
        var bar = new ScrollBar { Orientation = orientation, Minimum = 10, Maximum = 210, Value = 60, ViewportSize = 40 };
        using var host = new Host(bar);
        await host.LayoutAsync();
        AssertComfortBar(bar);
        var track = TrackOf(bar);
        Assert.Equal(orientation, track.Orientation);
        Assert.Equal(orientation == Orientation.Vertical, track.IsDirectionReversed);
        Assert.Equal(bar.Minimum, track.Minimum);
        Assert.Equal(bar.Maximum, track.Maximum);
        Assert.Equal(bar.ViewportSize, track.ViewportSize);
        Assert.Equal(bar.Value, track.Value);
        Assert.Same(orientation == Orientation.Vertical ? ScrollBar.PageUpCommand : ScrollBar.PageLeftCommand, track.DecreaseRepeatButton.Command);
        Assert.Same(orientation == Orientation.Vertical ? ScrollBar.PageDownCommand : ScrollBar.PageRightCommand, track.IncreaseRepeatButton.Command);
        bar.Maximum = 10_000;
        bar.ViewportSize = 1;
        await host.LayoutAsync();
        Assert.InRange(orientation == Orientation.Vertical ? track.Thumb.ActualHeight : track.Thumb.ActualWidth, 24, 30);
    });

    [Fact]
    public Task NativeScrollViewer_PreservesPagingKeyboardWheelAndDragInBothDirections() => WpfTestThread.RunAsync(async () =>
    {
        var viewer = new ScrollViewer
        {
            Content = new Border { Width = 1200, Height = 1800, Background = Brushes.LightSteelBlue },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false
        };
        using var host = new Host(viewer);
        await host.LayoutAsync();
        var vertical = Descendants<ScrollBar>(viewer).Single(bar => bar.Orientation == Orientation.Vertical);
        var horizontal = Descendants<ScrollBar>(viewer).Single(bar => bar.Orientation == Orientation.Horizontal);
        AssertComfortBar(vertical);
        AssertComfortBar(horizontal);

        ExecutePage(TrackOf(vertical).IncreaseRepeatButton);
        ExecutePage(TrackOf(horizontal).IncreaseRepeatButton);
        await host.LayoutAsync();
        Assert.True(viewer.VerticalOffset > 0);
        Assert.True(viewer.HorizontalOffset > 0);
        ExecutePage(TrackOf(vertical).DecreaseRepeatButton);
        ExecutePage(TrackOf(horizontal).DecreaseRepeatButton);
        await host.LayoutAsync();
        Assert.Equal(0, viewer.VerticalOffset);
        Assert.Equal(0, viewer.HorizontalOffset);

        viewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.MouseWheelEvent });
        await host.LayoutAsync();
        Assert.True(viewer.VerticalOffset > 0);
        double beforeKey = viewer.VerticalOffset;
        viewer.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(viewer)!, Environment.TickCount, Key.Down)
            { RoutedEvent = Keyboard.KeyDownEvent });
        viewer.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(viewer)!, Environment.TickCount, Key.Right)
            { RoutedEvent = Keyboard.KeyDownEvent });
        await host.LayoutAsync();
        Assert.True(viewer.VerticalOffset > beforeKey);
        Assert.True(viewer.HorizontalOffset > 0);

        double beforeVerticalDrag = viewer.VerticalOffset;
        double beforeHorizontalDrag = viewer.HorizontalOffset;
        Drag(TrackOf(vertical).Thumb, 0, 35);
        Drag(TrackOf(horizontal).Thumb, 35, 0);
        await host.LayoutAsync();
        Assert.True(viewer.VerticalOffset > beforeVerticalDrag);
        Assert.True(viewer.HorizontalOffset > beforeHorizontalDrag);
        foreach (bool dark in new[] { false, true, false })
        {
            ApplyTheme(host.Window, dark);
            await host.LayoutAsync();
            Assert.Equal(((SolidColorBrush)host.Window.Resources["ToolboxMutedBrush"]).Color,
                ((SolidColorBrush)ThumbShape(vertical).Background).Color);
            Assert.Equal(((SolidColorBrush)host.Window.Resources["ToolboxMutedBrush"]).Color,
                ((SolidColorBrush)ThumbShape(horizontal).Background).Color);
            WpfTestSnapshot.SaveWindowContent(host.Window, 460, 320, $"comfort-scrollviewer-{(dark ? "dark" : "light")}.png");
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NativeGridTextBoxAndLongComboPopup_InheritTheScrollbarStyle(bool dark) => WpfTestThread.RunAsync(async () =>
    {
        var grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = Enumerable.Range(1, 200).ToArray(), Height = 160 };
        for (int column = 0; column < 6; column++)
            grid.Columns.Add(new DataGridTextColumn { Header = $"列 {column + 1}", Binding = new System.Windows.Data.Binding(), Width = 180 });
        var combo = new ComboBox { ItemsSource = Enumerable.Range(1, 100).Select(index => $"选项 {index:000}").ToArray(), MaxDropDownHeight = 140 };
        var input = new TextBox
        {
            Text = string.Join(Environment.NewLine, Enumerable.Range(1, 80).Select(index => $"详情 {index}: " + new string('x', 160))),
            Height = 80, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var panel = new StackPanel();
        panel.Children.Add(grid);
        panel.Children.Add(combo);
        panel.Children.Add(input);
        using var host = new Host(panel);
        ApplyTheme(host.Window, dark);
        await host.LayoutAsync();
        foreach (var bar in Descendants<ScrollBar>(grid).Concat(Descendants<ScrollBar>(input)).Where(bar => bar.IsVisible))
            AssertComfortBar(bar);
        Assert.Equal(2, Descendants<ScrollBar>(grid).Count(bar => bar.IsVisible));
        Assert.Equal(2, Descendants<ScrollBar>(input).Count(bar => bar.IsVisible));
        Assert.True(grid.EnableRowVirtualization && grid.EnableColumnVirtualization);
        Assert.InRange(Descendants<DataGridRow>(grid).Count(), 1, 30);
        combo.IsDropDownOpen = true;
        try
        {
            await host.LayoutAsync();
            var popup = Assert.IsType<Popup>(combo.Template.FindName("PART_Popup", combo));
            Assert.True(popup.IsOpen);
            var bar = Descendants<ScrollBar>(popup.Child).Single(candidate => candidate.IsVisible && candidate.Orientation == Orientation.Vertical);
            AssertComfortBar(bar);
            Assert.Equal(((SolidColorBrush)host.Window.Resources["ToolboxMutedBrush"]).Color, ((SolidColorBrush)ThumbShape(bar).Background).Color);
            ExecutePage(TrackOf(bar).IncreaseRepeatButton);
            await host.LayoutAsync();
            Assert.True(Descendants<ScrollViewer>(popup.Child).Single().VerticalOffset > 0);
            SavePopup(popup.Child, $"comfort-scrollbar-popup-{(dark ? "dark" : "light")}.png");
            combo.SelectedIndex = 70;
            Assert.Equal("选项 071", combo.SelectedItem);
        }
        finally { combo.IsDropDownOpen = false; }
        WpfTestSnapshot.SaveWindowContent(host.Window, 460, 320, $"comfort-scrollbar-controls-{(dark ? "dark" : "light")}.png");
    });

    private static void AssertComfortBar(ScrollBar bar)
    {
        Assert.True(bar.IsLoaded);
        Assert.InRange(bar.Orientation == Orientation.Vertical ? bar.ActualWidth : bar.ActualHeight, 11.5, 12.5);
        Assert.Equal(Colors.Transparent, ((SolidColorBrush)bar.Background).Color);
        var track = TrackOf(bar);
        Assert.Equal(2, Descendants<RepeatButton>(bar).Count());
        Assert.All(Descendants<RepeatButton>(bar), button => Assert.Null(button.Content));
        var shape = ThumbShape(bar);
        Assert.Equal(new CornerRadius(3), shape.CornerRadius);
        Assert.InRange(bar.Orientation == Orientation.Vertical ? shape.ActualWidth : shape.ActualHeight, 5.5, 6.5);
        Assert.Contains(track.Thumb.Template.Triggers.OfType<Trigger>(), trigger => trigger.Property == UIElement.IsMouseOverProperty);
        Assert.Contains(track.Thumb.Template.Triggers.OfType<Trigger>(), trigger => trigger.Property == Thumb.IsDraggingProperty);
    }

    private static Track TrackOf(ScrollBar bar) => Assert.IsType<Track>(bar.Template.FindName("PART_Track", bar));
    private static Border ThumbShape(ScrollBar bar) => Assert.Single(Descendants<Border>(TrackOf(bar).Thumb));

    private static void ExecutePage(RepeatButton button)
    {
        var command = Assert.IsAssignableFrom<RoutedCommand>(button.Command);
        Assert.True(command.CanExecute(null, button));
        command.Execute(null, button);
    }

    private static void Drag(Thumb thumb, double x, double y)
    {
        thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        thumb.RaiseEvent(new DragDeltaEventArgs(x, y) { RoutedEvent = Thumb.DragDeltaEvent });
        thumb.RaiseEvent(new DragCompletedEventArgs(x, y, false) { RoutedEvent = Thumb.DragCompletedEvent });
    }

    private static void ApplyTheme(Window window, bool dark)
    {
        var type = typeof(MainWindow).Assembly.GetType("ToolsBox.App.Infrastructure.ComfortAppearance", true)!;
        type.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [window, dark]);
        window.Background = (Brush)window.Resources["ToolboxSurfaceBrush"];
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "ToolsBox.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("ToolsBox repository was not found.");
    }

    private static void SavePopup(UIElement child, string name)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(child.RenderSize.Width), (int)Math.Ceiling(child.RenderSize.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(child);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, name));
        encoder.Save(stream);
    }

    private sealed class Host : IDisposable
    {
        public Host(FrameworkElement content, string dictionary = "ComfortControls.xaml")
        {
            Window = new Window
            {
                Width = 460, Height = 320, Content = content, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                ShowActivated = false, ShowInTaskbar = false, Opacity = 0, Background = Brushes.White
            };
            Window.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/宝哥工具箱;component/Themes/{dictionary}", UriKind.Relative)
            });
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
