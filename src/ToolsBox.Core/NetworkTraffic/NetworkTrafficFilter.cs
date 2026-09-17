using System.Globalization;

namespace ToolsBox.Core.NetworkTraffic;

public static class NetworkTrafficFilter
{
    public static IReadOnlyList<ApplicationTrafficSnapshot> Apply(
        IEnumerable<ApplicationTrafficSnapshot> source,
        string? searchText,
        bool activeOnly)
    {
        string search = searchText?.Trim() ?? string.Empty;

        return source
            .Where(item => !activeOnly || item.UploadBytesPerSecond > 0 || item.DownloadBytesPerSecond > 0)
            .Where(item => Matches(item, search))
            .OrderByDescending(item => item.UploadBytesPerSecond + item.DownloadBytesPerSecond)
            .ThenBy(item => item.ApplicationName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static bool Matches(ApplicationTrafficSnapshot item, string search)
    {
        if (search.Length == 0)
        {
            return true;
        }

        return item.ApplicationName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
               || (item.ExecutablePath?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
               || item.Processes.Any(process =>
                   process.Identity.ProcessId.ToString(CultureInfo.InvariantCulture)
                       .Contains(search, StringComparison.OrdinalIgnoreCase));
    }
}
