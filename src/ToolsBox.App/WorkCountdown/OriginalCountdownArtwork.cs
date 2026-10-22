using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ToolsBox.App.WorkCountdown;

/// <summary>Original clock robots drawn from geometric primitives, without external character artwork.</summary>
internal static class OriginalCountdownArtwork
{
    private static readonly Color[] Colors =
    [
        Color.FromRgb(66, 145, 220), Color.FromRgb(225, 160, 55), Color.FromRgb(58, 160, 136),
        Color.FromRgb(151, 113, 205), Color.FromRgb(224, 117, 111)
    ];

    internal static BitmapSource CreateFrame(int variant, int mood, bool walking, int frame)
        => Render(256, drawing => DrawRobot(drawing, variant, mood, walking, frame));

    internal static BitmapSource CreateReminder(int variant)
        => Render(1024, drawing =>
        {
            Brush accent = Brush(Colors[variant]);
            Pen orbit = Stroke(accent, 2);
            // An open clock orbit keeps the backdrop transparent, including all four corners.
            drawing.DrawEllipse(null, orbit, new Point(128, 132), 104, 104);
            for (int tick = 0; tick < 12; tick++)
            {
                double angle = tick * Math.PI / 6;
                drawing.DrawLine(orbit, CirclePoint(128, 132, 96, angle), CirclePoint(128, 132, 101, angle));
            }
            DrawRobot(drawing, variant, variant == 2 ? 1 : 2, variant == 3, variant);
            DrawSpark(drawing, accent, 37, 62, 7);
            DrawSpark(drawing, accent, 219, 91, 5);
            drawing.DrawEllipse(accent, null, new Point(211, 204), 4, 4);
        });

    private static BitmapSource Render(int side, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            drawing.PushTransform(new ScaleTransform(side / 256d, side / 256d));
            draw(drawing);
            drawing.Pop();
        }
        var image = new RenderTargetBitmap(side, side, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }

    private static void DrawRobot(DrawingContext drawing, int variant, int mood, bool walking, int frame)
    {
        Brush accent = Brush(Colors[variant]);
        Brush ink = Brush(Color.FromRgb(37, 57, 79));
        Brush screen = Brush(Color.FromRgb(24, 43, 61));
        Brush light = Brush(Color.FromRgb(232, 249, 255));
        Pen outline = Stroke(ink, 2.5);
        double phase = frame * Math.PI / 3;
        double stride = walking ? Math.Sin(phase) * 12 : Math.Sin(phase) * 2;
        double bob = walking ? Math.Abs(Math.Sin(phase)) * -3 : Math.Sin(phase) * 1.5;
        drawing.PushTransform(new TranslateTransform(0, bob));

        // Rod limbs, a rectangular display and a clock chest establish a mechanical silhouette.
        Pen limb = Stroke(ink, 9);
        drawing.DrawLine(limb, new Point(107, 197), new Point(103 + stride, 222));
        drawing.DrawLine(limb, new Point(149, 197), new Point(153 - stride, 222));
        drawing.DrawRoundedRectangle(accent, outline, new Rect(91 + stride, 218, 26, 10), 4, 4);
        drawing.DrawRoundedRectangle(accent, outline, new Rect(139 - stride, 218, 26, 10), 4, 4);
        drawing.DrawLine(limb, new Point(86, 151), new Point(65 - stride / 4, 180 + stride / 3));
        drawing.DrawLine(limb, new Point(170, 151), new Point(191 + stride / 4, 180 - stride / 3));
        drawing.DrawEllipse(accent, outline, new Point(65 - stride / 4, 182 + stride / 3), 9, 9);
        drawing.DrawEllipse(accent, outline, new Point(191 + stride / 4, 182 - stride / 3), 9, 9);
        drawing.DrawRoundedRectangle(accent, outline, new Rect(84, 143, 88, 62), 14, 14);
        drawing.DrawRoundedRectangle(ink, null, new Rect(113, 131, 30, 15), 4, 4);

        drawing.DrawLine(Stroke(ink, 4), new Point(128, 53), new Point(128, 31));
        drawing.DrawEllipse(accent, outline, new Point(128, 27), 6, 6);
        drawing.DrawRoundedRectangle(accent, outline, new Rect(69, 51, 118, 85), 19, 19);
        drawing.DrawRoundedRectangle(screen, null, new Rect(81, 63, 94, 55), 11, 11);
        DrawFace(drawing, mood, light);
        drawing.DrawLine(Stroke(light, 3), new Point(111, 126), new Point(145, 126));

        drawing.DrawEllipse(light, outline, new Point(128, 174), 21, 21);
        Pen hands = Stroke(ink, 3);
        drawing.DrawLine(hands, new Point(128, 174), CirclePoint(128, 174, 15, -Math.PI / 2 + frame * 0.14));
        drawing.DrawLine(hands, new Point(128, 174), CirclePoint(128, 174, 10, variant * Math.PI / 5));
        drawing.DrawEllipse(ink, null, new Point(128, 174), 2.5, 2.5);
        drawing.Pop();
    }

    private static void DrawFace(DrawingContext drawing, int mood, Brush light)
    {
        Pen expression = Stroke(light, 3);
        foreach (double center in new[] { 106d, 150d })
        {
            if (mood == 1)
                drawing.DrawLine(expression, new Point(center - 5, 87), new Point(center + 5, 87));
            else if (mood == 2)
                Curve(drawing, expression, new Point(center - 5, 86), new Point(center - 3, 79),
                    new Point(center + 3, 79), new Point(center + 5, 86));
            else
            {
                drawing.DrawEllipse(light, null, new Point(center, 85), 4, mood == 3 ? 6 : 5);
                if (mood == 3)
                {
                    double slope = center < 128 ? 3 : -3;
                    drawing.DrawLine(expression, new Point(center - 6, 74 - slope), new Point(center + 6, 74 + slope));
                }
            }
        }
        if (mood == 2)
            Curve(drawing, expression, new Point(118, 101), new Point(123, 110), new Point(133, 110), new Point(138, 101));
        else if (mood == 3)
            Curve(drawing, expression, new Point(120, 106), new Point(124, 101), new Point(132, 101), new Point(136, 106));
        else
            drawing.DrawLine(expression, new Point(122, 103), new Point(134, 103));
    }

    private static void Curve(DrawingContext drawing, Pen pen, Point start, Point first, Point second, Point end)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(start, false, false);
            path.BezierTo(first, second, end, true, false);
        }
        geometry.Freeze();
        drawing.DrawGeometry(null, pen, geometry);
    }

    private static void DrawSpark(DrawingContext drawing, Brush brush, double x, double y, double radius)
    {
        Pen pen = Stroke(brush, 2);
        drawing.DrawLine(pen, new Point(x - radius, y), new Point(x + radius, y));
        drawing.DrawLine(pen, new Point(x, y - radius), new Point(x, y + radius));
    }

    private static Point CirclePoint(double x, double y, double radius, double angle)
        => new(x + Math.Cos(angle) * radius, y + Math.Sin(angle) * radius);

    private static Brush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen Stroke(Brush brush, double width)
    {
        var pen = new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }
}
