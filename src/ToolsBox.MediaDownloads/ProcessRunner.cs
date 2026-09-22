using System.Diagnostics;
using System.Text;
namespace ToolsBox.MediaDownloads;

public record ProcessResult(int ExitCode, string Output, string Error);
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken ct = default, Action<string>? outputReceived = null, string? workingDirectory = null)
    {
        ct.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (workingDirectory != null) start.WorkingDirectory = Path.GetFullPath(workingDirectory);
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        start.Environment["YTDLP_NO_PLUGINS"] = "1";
        using var process = new Process { StartInfo = start }; process.Start();
        using var registration = ct.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
        var stdout = ReadBounded(process.StandardOutput, ct, outputReceived); var stderr = ReadBounded(process.StandardError, ct, null);
        var exit = process.WaitForExitAsync(ct);
        try
        {
            var pending = new List<Task> { stdout, stderr, exit };
            while (pending.Count > 0) { var done = await Task.WhenAny(pending); await done; pending.Remove(done); }
            return new(process.ExitCode, await stdout, await stderr);
        }
        finally { if (!process.HasExited) { try { process.Kill(true); } catch (InvalidOperationException) { } await process.WaitForExitAsync(CancellationToken.None); } try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { } }
    }
    private static async Task<string> ReadBounded(StreamReader reader, CancellationToken ct, Action<string>? outputReceived)
    {
        var text = new StringBuilder(); var buffer = new char[8192]; int read;
        var line = new StringBuilder();
        while ((read = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0) { if (text.Length + read > 8 * 1024 * 1024) throw new InvalidDataException("组件输出超过 8 MB 限制。"); text.Append(buffer, 0, read); if (outputReceived != null) foreach (var c in buffer.AsSpan(0, read).ToArray()) { if (c == '\n') { outputReceived(line.ToString()); line.Clear(); } else if (line.Length < 4096) line.Append(c); } }
        return text.ToString();
    }
}
