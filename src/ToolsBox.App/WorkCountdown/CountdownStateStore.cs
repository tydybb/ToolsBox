using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolsBox.App.WorkCountdown;

public enum CountdownDayMode { Automatic, Workday, RestDay }

public sealed record CountdownState(
    [property: JsonRequired] DateOnly WorkDate,
    [property: JsonRequired] string StartTime,
    [property: JsonRequired] CountdownDayMode Mode,
    [property: JsonRequired] int OvertimeHours,
    DateTime? FinishedAt = null);

public interface ICountdownStateStore
{
    CountdownState? Load();
    void Save(CountdownState state);
    void Clear();
}

public sealed class JsonCountdownStateStore : ICountdownStateStore
{
    private readonly string _path;

    public JsonCountdownStateStore(string? path = null) => _path = Path.GetFullPath(path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolsBox", "work-countdown.json"));

    public CountdownState? Load()
    {
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 65536) throw new InvalidDataException("倒计时记录过大。");
            return JsonSerializer.Deserialize<CountdownState>(stream)
                ?? throw new InvalidDataException("倒计时记录为空。");
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public void Save(CountdownState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state);
                stream.Flush(true);
            }
            File.Move(temporary, _path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public void Clear() => File.Delete(_path);
}
