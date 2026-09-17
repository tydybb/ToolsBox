namespace ToolsBox.Core.Ports;

public static class PortEntryFilter
{
    public static IReadOnlyList<PortEntry> Apply(IEnumerable<PortEntry> entries, string? query)
    {
        ArgumentNullException.ThrowIfNull(entries);

        string term = query?.Trim() ?? string.Empty;
        if (term.Length == 0)
        {
            return entries.ToArray();
        }

        return entries.Where(entry => Matches(entry, term)).ToArray();
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
