using System.Globalization;

namespace ToolsBox.Core.Ports;

public enum PortSearchField
{
    All,
    Port,
    IpAddress,
    ProcessId,
    ProcessName
}

public static class PortEntryFilter
{
    public static IReadOnlyList<PortEntry> Apply(
        IEnumerable<PortEntry> entries, string? query, PortSearchField field = PortSearchField.All)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (!Enum.IsDefined(field))
        {
            throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown port search field.");
        }

        string term = query?.Trim() ?? string.Empty;
        if (term.Length == 0)
        {
            return entries.ToArray();
        }

        return field switch
        {
            PortSearchField.All => entries.Where(entry => Matches(entry, term)).ToArray(),
            PortSearchField.Port =>
                int.TryParse(term, NumberStyles.None, CultureInfo.InvariantCulture, out int port)
                    && port <= ushort.MaxValue
                    ? entries.Where(entry => entry.LocalPort == port || entry.RemotePort == port).ToArray()
                    : [],
            PortSearchField.IpAddress => entries.Where(entry =>
                entry.LocalAddress.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || entry.RemoteAddress.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray(),
            PortSearchField.ProcessId =>
                int.TryParse(term, NumberStyles.None, CultureInfo.InvariantCulture, out int processId)
                    ? entries.Where(entry => entry.ProcessId == processId).ToArray()
                    : [],
            PortSearchField.ProcessName => entries.Where(entry =>
                entry.ProcessName.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown port search field.")
        };
    }

    private static bool Matches(PortEntry entry, string term)
    {
        StringComparison comparison = StringComparison.OrdinalIgnoreCase;
        return entry.Protocol.Contains(term, comparison)
            || entry.AddressFamilyDisplay.Contains(term, comparison)
            || entry.LocalAddress.Contains(term, comparison)
            || entry.LocalPort.ToString().Contains(term, comparison)
            || entry.RemoteAddress.Contains(term, comparison)
            || (entry.RemotePort?.ToString().Contains(term, comparison) ?? false)
            || entry.State.Contains(term, comparison)
            || entry.ProcessId.ToString().Contains(term, comparison)
            || entry.ProcessName.Contains(term, comparison);
    }
}
