using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.App.Attendance;

public sealed record AttendanceOptions
{
    public bool Enabled { get; init; }
    public string AccountDirectory { get; init; } = "";
    public DateOnly? ManualConfirmedOn { get; init; }
    public DateOnly? OverrideDate { get; init; }
    public bool? OverrideWorkday { get; init; }
    public DateTime? CheckRequestedAt { get; init; }
    public DateTime? HandledClockIn { get; init; }
    public bool IsWorkday(DateOnly date) => OverrideDate == date && OverrideWorkday.HasValue
        ? OverrideWorkday.Value : ChinaWorkCalendar.GetDay(date).Kind == DayKind.Workday;
}

public sealed record AttendanceSnapshot(DateTime CheckedAt, DateTime? ClockInAt, string AccountDirectory,
    string Message, DateTime? LastReminder = null, DateOnly? FinalReminderDate = null, DateTime? CompletedRequest = null);

/// <summary>Only minimal status is persisted; no messages, keys, cookies or database copies.</summary>
public sealed class AttendanceStore
{
    private readonly string _root;
    private readonly string _mutexName;
    public AttendanceStore(string? root = null)
    {
        _root = Path.GetFullPath(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolsBox", "attendance"));
        _mutexName = "Local\\ToolsBox.Attendance.Settings." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_root.ToUpperInvariant())))[..20];
    }
    public AttendanceOptions LoadOptions() => Read<AttendanceOptions>("options.json") ?? new();
    public AttendanceSnapshot? LoadSnapshot() => Read<AttendanceSnapshot>("status.json");
    public void SaveSnapshot(AttendanceSnapshot value) => Write("status.json", value);
    public AttendancePreviewRequest? LoadPreviewRequest()=>Read<AttendancePreviewRequest>("preview-request.json");
    public AttendancePreviewResult? LoadPreviewResult()=>Read<AttendancePreviewResult>("preview-result.json");
    public void SavePreviewRequest(AttendancePreviewRequest value)=>Write("preview-request.json",value);
    public void SavePreviewResult(AttendancePreviewResult value)=>Write("preview-result.json",value);
    public void Update(Func<AttendanceOptions, AttendanceOptions> change)
    {
        using var mutex = new Mutex(false, _mutexName);
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(2)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("打卡设置正忙，请重试。");
        try { Write("options.json", change(LoadOptions())); }
        finally { mutex.ReleaseMutex(); }
    }
    private T? Read<T>(string name)
    {
        try
        {
            using var input = new FileStream(Path.Combine(_root,name), FileMode.Open,FileAccess.Read,FileShare.ReadWrite | FileShare.Delete);
            if(input.Length > 16384) throw new InvalidDataException("打卡设置文件过大。");
            return JsonSerializer.Deserialize<T>(input);
        }
        catch(FileNotFoundException) { return default; }
        catch(DirectoryNotFoundException) { return default; }
    }
    private void Write<T>(string name,T value)
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root,name), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using(var output = new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            { JsonSerializer.Serialize(output,value); output.Flush(true); }
            File.Move(temporary,path,true);
        }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
}
