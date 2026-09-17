namespace ToolsBox.Core.FileUnlocking;

public static class FileLockPathMatcher
{
    public static bool Matches(FileLockTarget target, string candidatePath)
    {
        ArgumentNullException.ThrowIfNull(target);
        string candidate = Normalize(candidatePath);
        if (string.Equals(target.Path, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!target.IsDirectory)
        {
            return false;
        }

        string prefix = target.Path.EndsWith(Path.DirectorySeparatorChar)
            ? target.Path
            : target.Path + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string value = path.Trim().Trim('"');
        if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            value = @"\\" + value[8..];
        }
        else if (value.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            value = value[4..];
        }

        value = Path.GetFullPath(value);
        string root = Path.GetPathRoot(value) ?? string.Empty;
        if (!string.Equals(value, root, StringComparison.OrdinalIgnoreCase))
        {
            value = value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return value;
    }
}
