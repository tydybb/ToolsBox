namespace ToolsBox.Core.FileUnlocking;

public sealed record FileLockTarget(string Path, bool IsDirectory)
{
    public static FileLockTarget FromExistingPath(string path)
    {
        string normalized = FileLockPathMatcher.Normalize(path);
        if (File.Exists(normalized))
        {
            return new FileLockTarget(normalized, false);
        }

        if (Directory.Exists(normalized))
        {
            return new FileLockTarget(normalized, true);
        }

        throw new FileNotFoundException("指定的文件或文件夹不存在。", normalized);
    }

    public static FileLockTarget Create(string path, bool isDirectory) =>
        new(FileLockPathMatcher.Normalize(path), isDirectory);
}
