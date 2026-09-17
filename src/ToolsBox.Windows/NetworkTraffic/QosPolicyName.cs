using System.Security.Cryptography;
using System.Text;

namespace ToolsBox.Windows.NetworkTraffic;

public static class QosPolicyName
{
    public const string Prefix = "BaoGeToolsBox-";

    public static string ForPath(string executablePath)
    {
        string normalized = NormalizePath(executablePath);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Prefix + Convert.ToHexString(hash.AsSpan(0, 12));
    }

    public static bool IsOwned(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.StartsWith(Prefix, StringComparison.Ordinal);

    public static string NormalizePath(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return Path.GetFullPath(executablePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
    }
}
