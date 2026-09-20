using System.IO;
using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class CountdownStateStoreTests
{
    [Fact]
    public void LegacyJsonLoadsWithoutCompletionAndFinishedTimestampRoundTrips()
    {
        const string json = "{\"WorkDate\":\"2026-09-18\",\"StartTime\":\"09:30\",\"Mode\":1,\"OvertimeHours\":2}";
        var legacy = System.Text.Json.JsonSerializer.Deserialize<CountdownState>(json)!;
        Assert.Null(legacy.FinishedAt);
        var finished = legacy with { FinishedAt = new DateTime(2026, 9, 18, 18, 30, 12) };
        Assert.Equal(finished, System.Text.Json.JsonSerializer.Deserialize<CountdownState>(System.Text.Json.JsonSerializer.Serialize(finished)));
    }

    [Fact]
    public void FileStore_RoundTrips_Replaces_AndClears()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ToolsBox-countdown-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "state.json");
        try
        {
            var store = new JsonCountdownStateStore(path);
            Assert.Null(store.Load());
            var state = new CountdownState(new(2026, 9, 18), "09:30", CountdownDayMode.Automatic, 2);
            store.Save(state);
            Assert.Equal(state, store.Load());
            state = state with { OvertimeHours = 3, FinishedAt = new DateTime(2026, 9, 18, 18, 30, 0) };
            store.Save(state);
            Assert.Equal(state, store.Load());
            using var restored = new WorkCountdownViewModel(() => new(2026, 9, 18, 20, 0, 0), store, false);
            Assert.True(restored.IsFinished);
            Assert.Equal("03:30:00", restored.TimerText);
            Assert.False(restored.IsOverdue);
            Assert.Single(Directory.GetFiles(directory));
            store.Clear();
            Assert.Null(store.Load());
            store.Clear();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    [Fact]
    public void CorruptSavedState_ShowsWarningWithoutStarting()
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 9, 0, 0), new CorruptStore(), false);
        Assert.Null(vm.Schedule);
        Assert.Contains("恢复", vm.ErrorMessage);
    }

    private sealed class CorruptStore : ICountdownStateStore
    {
        public CountdownState? Load() => throw new System.Text.Json.JsonException("broken");
        public void Save(CountdownState state) { }
        public void Clear() { }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullOrOversizedFile_DoesNotPreventApplicationStartup(bool oversized)
    {
        string path = Path.Combine(Path.GetTempPath(), "ToolsBox-countdown-invalid-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, oversized ? new string(' ', 70000) : "null");
            using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 9, 0, 0), new JsonCountdownStateStore(path), false);
            Assert.Null(vm.Schedule);
            Assert.Contains("恢复", vm.ErrorMessage);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("{\"WorkDate\":\"2026-09-18\",\"StartTime\":\"08:30\"}")]
    [InlineData("{\"WorkDate\":\"2026-09-18\",\"StartTime\":\"08:30\",\"Mode\":0}")]
    [InlineData("{\"WorkDate\":\"2026-09-18\",\"StartTime\":\"08:30\",\"OvertimeHours\":4}")]
    [InlineData("{\"StartTime\":\"08:30\",\"Mode\":0,\"OvertimeHours\":0}")]
    [InlineData("{\"WorkDate\":\"2026-09-18\",\"Mode\":0,\"OvertimeHours\":0}")]
    [InlineData("{\"WorkDate\":\"2026-09-18\",\"StartTime\":null,\"Mode\":0,\"OvertimeHours\":0}")]
    public void IncompleteSavedFile_ShowsWarningInsteadOfGuessingDefaults(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), "ToolsBox-countdown-incomplete-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, json);
            using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 9, 0, 0), new JsonCountdownStateStore(path), false);
            Assert.Null(vm.Schedule);
            Assert.Contains("恢复", vm.ErrorMessage);
        }
        finally { File.Delete(path); }
    }
}
