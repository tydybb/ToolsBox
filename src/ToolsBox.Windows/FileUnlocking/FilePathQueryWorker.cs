using System.Text.Json;

namespace ToolsBox.Windows.FileUnlocking;

public static class FilePathQueryWorker
{
    public static void Run()
    {
        using var input = new StreamReader(Console.OpenStandardInput());
        using var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        output.WriteLine("ToolsBox.FilePathQuery.v1");
        while (input.ReadLine() is { } line)
        {
            if (!long.TryParse(line, out long value) || value <= 0) return;
            IntPtr handle = new(value);
            string? path;
            try { path = SystemHandleScanner.TryGetPath(handle); }
            finally { FileHandleNativeMethods.CloseHandle(handle); }
            output.WriteLine(JsonSerializer.Serialize(path));
        }
    }
}
