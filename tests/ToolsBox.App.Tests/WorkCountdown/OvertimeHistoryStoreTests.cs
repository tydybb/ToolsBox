using System.IO;
using System.Text.Json;
using ToolsBox.App.WorkCountdown;
using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class OvertimeHistoryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ToolsBox-overtime-test-" + Guid.NewGuid().ToString("N"));
    private string HistoryPath => Path.Combine(_directory, "history.json");

    [Fact]
    public void MissingFileIsEmptyAndValidRecordsRoundTripInDateOrder()
    {
        var store = new JsonOvertimeHistoryStore(HistoryPath);
        Assert.Empty(store.Load());
        var september = Record(new DateOnly(2026, 9, 30), 20);
        var october = Record(new DateOnly(2026, 10, 1), 17);
        store.Upsert(september);
        store.Upsert(october);

        Assert.Equal(new[] { october, september }, new JsonOvertimeHistoryStore(HistoryPath).Load());
        Assert.Equal(TimeSpan.Zero, october.Duration);
        Assert.DoesNotContain("Duration", File.ReadAllText(HistoryPath));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void SameDayReplacesOneRecordAndRetainsOlderMonths()
    {
        var first = new JsonOvertimeHistoryStore(HistoryPath);
        var second = new JsonOvertimeHistoryStore(HistoryPath);
        var older = Record(new DateOnly(2026, 8, 30), 20);
        var current = Record(new DateOnly(2026, 10, 1), 19);
        first.Upsert(older);
        second.Upsert(current);
        var updated = Record(current.WorkDate, 21);
        first.Upsert(updated);

        Assert.Equal(new[] { updated, older }, second.Load());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RecoveryOnlyReplacesAnEarlierFinishWhileExplicitSaveCanReplaceAnyFinish(int storeKind)
    {
        IOvertimeHistoryStore store = storeKind switch
        {
            0 => new JsonOvertimeHistoryStore(HistoryPath),
            1 => new MemoryOvertimeHistoryStore(),
            _ => new LegacyHistoryStore()
        };
        var date = new DateOnly(2026, 10, 1);
        var saved = Record(date, 21);
        store.Upsert(saved);
        store.UpsertIfNewer(Record(date, 20));
        Assert.Equal(saved, Assert.Single(store.Load()));

        var equalFinish = saved with { StartedAt = saved.StartedAt.AddMinutes(-15) };
        store.UpsertIfNewer(equalFinish);
        Assert.Equal(saved, Assert.Single(store.Load()));

        var later = Record(date, 22);
        store.UpsertIfNewer(later);
        Assert.Equal(later, Assert.Single(store.Load()));
        var explicitCorrection = Record(date, 20);
        store.Upsert(explicitCorrection);
        Assert.Equal(explicitCorrection, Assert.Single(store.Load()));

        var anotherDate = Record(date.AddDays(1), 20);
        store.UpsertIfNewer(anotherDate);
        Assert.Equal(new[] { anotherDate, explicitCorrection }, store.Load());
    }

    [Fact]
    public async Task BusyRecoveryLeavesTheFileIntactAndLaterRetryComparesLatestFile()
    {
        var first = new JsonOvertimeHistoryStore(HistoryPath);
        var second = new JsonOvertimeHistoryStore(HistoryPath);
        var date = new DateOnly(2026, 10, 1);
        var saved = Record(date, 21);
        first.Upsert(saved);
        string original = File.ReadAllText(HistoryPath);
        string mutexName = (string)typeof(JsonOvertimeHistoryStore).GetField(
            "_mutexName", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(first)!;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            using var mutex = new Mutex(false, mutexName);
            mutex.WaitOne();
            try
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(15));
            }
            finally { mutex.ReleaseMutex(); }
        });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAsync<IOException>(async () =>
                await Task.Run(() => first.UpsertIfNewer(Record(date, 20))).WaitAsync(TimeSpan.FromSeconds(6)));
            Assert.Equal(original, File.ReadAllText(HistoryPath));
        }
        finally
        {
            release.Set();
            await holder;
        }

        var latest = Record(date, 22);
        second.Upsert(latest);
        first.UpsertIfNewer(Record(date, 20));
        Assert.Equal(latest, Assert.Single(second.Load()));
    }

    [Fact]
    public async Task ConcurrentInstancesDoNotLoseDifferentDays()
    {
        var stores = Enumerable.Range(1, 12).Select(_ => new JsonOvertimeHistoryStore(HistoryPath)).ToArray();
        await Task.WhenAll(stores.Select((store, index) => Task.Run(() =>
            store.Upsert(Record(new DateOnly(2026, 10, index + 1), 20)))));

        Assert.Equal(12, stores[0].Load().Count);
    }

    [Fact]
    public async Task BusyHistoryReturnsAnActionableErrorWithinBoundedTime()
    {
        var store = new JsonOvertimeHistoryStore(HistoryPath);
        string mutexName = (string)typeof(JsonOvertimeHistoryStore).GetField(
            "_mutexName", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(store)!;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            using var mutex = new Mutex(false, mutexName);
            mutex.WaitOne();
            try
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(15));
            }
            finally { mutex.ReleaseMutex(); }
        });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var error = await Assert.ThrowsAsync<IOException>(async () =>
                await Task.Run(() => store.Load()).WaitAsync(TimeSpan.FromSeconds(6)));
            Assert.Contains("刷新", error.Message);
        }
        finally
        {
            release.Set();
            await holder;
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"Version\":2,\"Records\":[]}")]
    [InlineData("{\"Version\":1,\"Records\":null}")]
    [InlineData("{\"Version\":1,\"Records\":[null]}")]
    [InlineData("{\"Version\":1,\"Records\":[{}]}")]
    public void CorruptDocumentFailsWithoutOverwritingExistingBytes(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(HistoryPath, json);
        var store = new JsonOvertimeHistoryStore(HistoryPath);
        Assert.Throws<InvalidDataException>(() => store.Load());
        Assert.Throws<InvalidDataException>(() => store.Upsert(Record(new DateOnly(2026, 10, 1), 20)));
        Assert.Throws<InvalidDataException>(() => store.UpsertIfNewer(Record(new DateOnly(2026, 10, 1), 20)));
        Assert.Equal(json, File.ReadAllText(HistoryPath));
    }

    [Fact]
    public void OversizedHistoryFailsWithoutOverwriting()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(HistoryPath, new string(' ', 1_048_577));
        var store = new JsonOvertimeHistoryStore(HistoryPath);
        Assert.Throws<InvalidDataException>(() => store.Load());
        Assert.Throws<InvalidDataException>(() => store.Upsert(Record(new DateOnly(2026, 10, 1), 20)));
        Assert.Equal(1_048_577, new FileInfo(HistoryPath).Length);
    }

    [Fact]
    public void DuplicateDatesAndEveryMissingRecordFieldAreRejected()
    {
        Directory.CreateDirectory(_directory);
        var record = Record(new DateOnly(2026, 10, 1), 20);
        WriteDocument(new[] { record, record });
        Assert.Throws<InvalidDataException>(() => new JsonOvertimeHistoryStore(HistoryPath).Load());
        foreach (string field in new[] { "WorkDate", "StartedAt", "NormalEnd", "FinishedAt", "OvertimeTicks" })
        {
            var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(record))!;
            fields.Remove(field);
            File.WriteAllText(HistoryPath, JsonSerializer.Serialize(new { Version = 1, Records = new[] { fields } }));
            Assert.Throws<InvalidDataException>(() => new JsonOvertimeHistoryStore(HistoryPath).Load());
        }
    }

    public static IEnumerable<object[]> InvalidRecords()
    {
        var record = Record(new DateOnly(2026, 10, 1), 20);
        yield return new object[] { record with { WorkDate = record.WorkDate.AddDays(1) } };
        yield return new object[] { record with { FinishedAt = record.StartedAt.AddTicks(-1) } };
        yield return new object[] { record with { NormalEnd = record.StartedAt.AddTicks(-1) } };
        yield return new object[] { record with { NormalEnd = record.StartedAt.AddDays(1) } };
        yield return new object[] { record with { OvertimeTicks = -1 } };
        yield return new object[] { record with { OvertimeTicks = 0 } };
        yield return new object[] { record with { OvertimeTicks = long.MaxValue } };
    }

    [Theory]
    [MemberData(nameof(InvalidRecords))]
    public void InvalidRecordsAreRejectedOnReadAndWrite(OvertimeRecord record)
    {
        var store = new JsonOvertimeHistoryStore(HistoryPath);
        Assert.Throws<InvalidDataException>(() => store.Upsert(record));
        Assert.False(File.Exists(HistoryPath));
        WriteDocument(new[] { record });
        Assert.Throws<InvalidDataException>(() => store.Load());
    }

    [Fact]
    public void RestDayRecordCanSpanMonthsAndMemoryStoresAreIndependent()
    {
        var start = new DateTime(2026, 9, 30, 8, 0, 0);
        var finish = new DateTime(2026, 10, 1, 14, 0, 0);
        var record = new OvertimeRecord(DateOnly.FromDateTime(start), start, null, finish, TimeSpan.FromHours(27.5).Ticks);
        var memory = new MemoryOvertimeHistoryStore();
        memory.Upsert(record);
        Assert.Equal(record, Assert.Single(memory.Load()));
        Assert.Empty(new MemoryOvertimeHistoryStore().Load());
        var file = new JsonOvertimeHistoryStore(HistoryPath);
        file.Upsert(record);
        Assert.Equal(record, Assert.Single(file.Load()));
    }

    [Fact]
    public void ArrivalAfterNormalEndOnlyCountsTimeAfterArrival()
    {
        var date = new DateOnly(2026, 10, 1);
        var record = new OvertimeRecord(date, date.ToDateTime(new TimeOnly(19, 30)),
            date.ToDateTime(new TimeOnly(18, 30)), date.ToDateTime(new TimeOnly(20, 0)), TimeSpan.FromMinutes(30).Ticks);
        var store = new JsonOvertimeHistoryStore(HistoryPath);
        store.Upsert(record);
        Assert.Equal(record, Assert.Single(store.Load()));
        Assert.Throws<InvalidDataException>(() => store.Upsert(record with { OvertimeTicks = TimeSpan.FromHours(1).Ticks }));
    }

    private void WriteDocument(OvertimeRecord[] records)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(HistoryPath, JsonSerializer.Serialize(new { Version = 1, Records = records }));
    }

    private static OvertimeRecord Record(DateOnly date, int finishHour)
    {
        var start = date.ToDateTime(new TimeOnly(8, 0));
        var normalEnd = date.ToDateTime(new TimeOnly(17, 0));
        var finish = date.ToDateTime(new TimeOnly(finishHour, 0));
        var overtimeMinutes = finishHour <= 19 ? (finishHour - 17) * 60 : (finishHour - 17) * 60 - 30;
        return new(date, start, normalEnd, finish, TimeSpan.FromMinutes(overtimeMinutes).Ticks);
    }

    private sealed class LegacyHistoryStore : IOvertimeHistoryStore
    {
        private readonly MemoryOvertimeHistoryStore _inner = new();
        public IReadOnlyList<OvertimeRecord> Load() => _inner.Load();
        public void Upsert(OvertimeRecord record) => _inner.Upsert(record);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
