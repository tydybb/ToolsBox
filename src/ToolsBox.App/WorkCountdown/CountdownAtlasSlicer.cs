using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ToolsBox.App.WorkCountdown;

/// <summary>Finds the actual transparent gutters instead of slicing through generated ears and feet.</summary>
internal static class CountdownAtlasSlicer
{
    private const byte VisibleAlpha = 4;
    private const int Padding = 10;

    internal static IReadOnlyList<BitmapSource>[] Align(BitmapSource source, int columns, int rows)
    {
        int width = source.PixelWidth, height = source.PixelHeight;
        int frameWidth = width / columns, frameHeight = height / rows;
        if (frameWidth <= Padding * 2 || frameHeight <= Padding * 2) return [];
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[width * height * 4];
        converted.CopyPixels(pixels, width * 4, 0);
        var occupiedX = new bool[width];
        var occupiedY = new bool[height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            if (pixels[(y * width + x) * 4 + 3] <= VisibleAlpha) continue;
            occupiedX[x] = true; occupiedY[y] = true;
        }
        int[]? xCuts = FindCuts(occupiedX, columns);
        int[]? yCuts = FindCuts(occupiedY, rows);
        // A broken or crowded atlas must not silently leak a neighbour into another frame.
        if (xCuts is null || yCuts is null) return [];

        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            bool hasContent = false;
            for (int y = yCuts[row]; y < yCuts[row + 1]; y++)
            for (int x = xCuts[column]; x < xCuts[column + 1]; x++)
            {
                if (pixels[(y * width + x) * 4 + 3] <= VisibleAlpha) continue;
                hasContent = true;
                int localX = x - column * frameWidth, localY = y - row * frameHeight;
                left = Math.Min(left, localX); top = Math.Min(top, localY);
                right = Math.Max(right, localX + 1); bottom = Math.Max(bottom, localY + 1);
            }
            if (!hasContent) return [];
        }

        // One scale and origin for the whole sheet: never resize/recentre each walking pose independently.
        double scale = Math.Min((double)(frameWidth - Padding * 2) / (right - left),
            (double)(frameHeight - Padding * 2) / (bottom - top));
        double originX = (frameWidth - (right - left) * scale) / 2 - left * scale;
        double originY = frameHeight - Padding - bottom * scale;
        var clip = new RectangleGeometry(new Rect(Padding, Padding, frameWidth - Padding * 2, frameHeight - Padding * 2));
        clip.Freeze();
        var atlas = new IReadOnlyList<BitmapSource>[rows];
        for (int row = 0; row < rows; row++)
        {
            var frames = new BitmapSource[columns];
            for (int column = 0; column < columns; column++)
            {
                var rect = new Int32Rect(xCuts[column], yCuts[row],
                    xCuts[column + 1] - xCuts[column], yCuts[row + 1] - yCuts[row]);
                var cropped = new CroppedBitmap(source, rect);
                cropped.Freeze();
                var visual = new DrawingVisual();
                RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
                using (DrawingContext drawing = visual.RenderOpen())
                {
                    drawing.PushClip(clip);
                    drawing.DrawImage(cropped, new Rect(originX + (rect.X - column * frameWidth) * scale,
                        originY + (rect.Y - row * frameHeight) * scale, rect.Width * scale, rect.Height * scale));
                    drawing.Pop();
                }
                var frame = new RenderTargetBitmap(frameWidth, frameHeight, 96, 96, PixelFormats.Pbgra32);
                frame.Render(visual);
                frame.Freeze();
                frames[column] = frame;
            }
            atlas[row] = Array.AsReadOnly(frames);
        }
        return atlas;
    }

    private static int[]? FindCuts(bool[] occupied, int divisions)
    {
        var cuts = new int[divisions + 1];
        cuts[^1] = occupied.Length;
        int cell = occupied.Length / divisions, radius = cell / 4;
        for (int boundary = 1; boundary < divisions; boundary++)
        {
            int nominal = boundary * cell;
            int lower = nominal - radius, upper = nominal + radius;
            int best = -1, bestDistance = int.MaxValue;
            for (int position = lower; position <= upper;)
            {
                if (occupied[position]) { position++; continue; }
                int start = position;
                while (position <= upper && !occupied[position]) position++;
                int middle = (start + position - 1) / 2;
                int distance = Math.Abs(middle - nominal);
                if (distance >= bestDistance) continue;
                best = middle; bestDistance = distance;
            }
            if (best <= cuts[boundary - 1]) return null;
            cuts[boundary] = best;
        }
        return cuts;
    }
}
