using System.Globalization;

namespace ToolsBox.App.NetworkTraffic;

public static class ByteRateFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string FormatBytes(long bytes) => Format(bytes, false);
    public static string FormatRate(long bytesPerSecond) => Format(bytesPerSecond, true);

    private static string Format(long value, bool perSecond)
    {
        double size = Math.Max(0, value);
        int unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        string number = unit == 0
            ? size.ToString("0", CultureInfo.InvariantCulture)
            : size.ToString(size >= 100 ? "0" : "0.0", CultureInfo.InvariantCulture);
        return $"{number} {Units[unit]}{(perSecond ? "/s" : string.Empty)}";
    }
}
