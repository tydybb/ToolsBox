using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.App.WorkCountdown;

public interface IOvertimeHistoryStore
{
    IReadOnlyList<OvertimeRecord> Load();
    void Upsert(OvertimeRecord record);

    void UpsertIfNewer(OvertimeRecord record)
    {
        if (record is null) throw new InvalidDataException("加班历史记录不能为空。");
        record.Validate();
        if (!Load().Any(existing => existing.WorkDate == record.WorkDate && existing.FinishedAt >= record.FinishedAt))
            Upsert(record);
    }
}

public sealed class JsonOvertimeHistoryStore : IOvertimeHistoryStore
{
    private const int MaximumBytes = 1_048_576;
    private readonly string _path;
    private readonly string _mutexName;

    public JsonOvertimeHistoryStore(string? path = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolsBox", "overtime-history.json"));
        _mutexName = "Local\\ToolsBox-OvertimeHistory-" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(_path.ToUpperInvariant())));
    }

    public IReadOnlyList<OvertimeRecord> Load() => WithMutex(() => ReadRecords()
        .OrderByDescending(record => record.WorkDate).ToArray());

    public void Upsert(OvertimeRecord record) => UpsertCore(record, false);

    public void UpsertIfNewer(OvertimeRecord record) => UpsertCore(record, true);

    private void UpsertCore(OvertimeRecord record, bool onlyIfNewer)
    {
        ValidateRecord(record);
        WithMutex(() =>
        {
            var records = ReadRecords();
            if (onlyIfNewer && records.Any(existing => existing.WorkDate == record.WorkDate && existing.FinishedAt >= record.FinishedAt))
                return false;
            records.RemoveAll(existing => existing.WorkDate == record.WorkDate);
            records.Add(record);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new HistoryDocument(1, records));
            if (bytes.Length > MaximumBytes) throw new InvalidDataException("加班历史记录超过 1 MiB。");

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                    stream.Flush(true);
                }
                if (File.Exists(_path)) File.Replace(temporary, _path, null);
                else File.Move(temporary, _path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            return true;
        });
    }

    private List<OvertimeRecord> ReadRecords()
    {
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumBytes) throw new InvalidDataException("加班历史记录超过 1 MiB。");
            HistoryDocument? document;
            try { document = JsonSerializer.Deserialize<HistoryDocument>(stream); }
            catch (JsonException exception) { throw new InvalidDataException("加班历史记录格式损坏。", exception); }
            if (document is null || document.Version != 1 || document.Records is null)
                throw new InvalidDataException("加班历史记录格式或版本无效。");

            var dates = new HashSet<DateOnly>();
            foreach (var record in document.Records)
            {
                ValidateRecord(record);
                if (!dates.Add(record.WorkDate)) throw new InvalidDataException("加班历史记录包含重复日期。");
            }
            return document.Records;
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
    }

    private T WithMutex<T>(Func<T> operation)
    {
        using var mutex = new Mutex(false, _mutexName);
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(2)))
                throw new IOException("加班记录正忙，请稍后点击刷新重试。");
        }
        catch (AbandonedMutexException) { }
        try { return operation(); }
        finally { mutex.ReleaseMutex(); }
    }

    private static void ValidateRecord(OvertimeRecord? record)
    {
        if (record is null) throw new InvalidDataException("加班历史记录不能为空。");
        record.Validate();
    }

    private sealed record HistoryDocument(
        [property: JsonRequired] int Version,
        [property: JsonRequired] List<OvertimeRecord> Records);
}

public sealed class MemoryOvertimeHistoryStore : IOvertimeHistoryStore
{
    private readonly Dictionary<DateOnly, OvertimeRecord> _records = [];
    private readonly object _gate = new();

    public IReadOnlyList<OvertimeRecord> Load()
    {
        lock (_gate) return _records.Values.OrderByDescending(record => record.WorkDate).ToArray();
    }

    public void Upsert(OvertimeRecord record) => UpsertCore(record, false);

    public void UpsertIfNewer(OvertimeRecord record) => UpsertCore(record, true);

    private void UpsertCore(OvertimeRecord record, bool onlyIfNewer)
    {
        if (record is null) throw new InvalidDataException("加班历史记录不能为空。");
        record.Validate();
        lock (_gate)
        {
            if (onlyIfNewer && _records.TryGetValue(record.WorkDate, out var existing) && existing.FinishedAt >= record.FinishedAt)
                return;
            _records[record.WorkDate] = record;
        }
    }
}
