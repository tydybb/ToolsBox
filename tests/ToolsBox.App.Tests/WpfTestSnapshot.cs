using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ToolsBox.App.Tests;

internal static class WpfTestSnapshot
{
    public static void SaveWindowContent(Window window, int width, int height, string fileName)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        var background = new DrawingVisual();
        using (DrawingContext drawing = background.RenderOpen())
            drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(background);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(AppContext.BaseDirectory, fileName));
        encoder.Save(output);
    }
}
