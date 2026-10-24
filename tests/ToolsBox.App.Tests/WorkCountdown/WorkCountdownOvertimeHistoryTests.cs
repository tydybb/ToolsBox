using System.Collections;
using System.IO;
using ToolsBox.App.Infrastructure;
using ToolsBox.App.WorkCountdown;
using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class WorkCountdownOvertimeHistoryTests
{
    [Fact]
    public void RecoveryAfterUnreadableHistoryNeverRollsBackANewerRecordedFinish()
    {
        DateTime start = new(2026, 10, 23, 9, 30, 0), normal = start.AddHours(9), later = start.Date.AddHours(21);
        var history = new FailingHistory();
        history.Upsert(new(new(2026, 10, 23), start, normal, later, TimeSpan.FromHours(2).Ticks));
        history.FailRead = true;
        var state = new StateStore { State = new(new(2026, 10, 23), "09:30", CountdownDayMode.Workday, 0, start.Date.AddHours(20)) };
        using var vm = new WorkCountdownViewModel(() => later.AddDays(1), state, false, history);
        Assert.NotEmpty(vm.OvertimeHistoryError);
        history.FailRead = false;
        vm.RefreshOvertimeHistoryCommand.Execute(null);
        Assert.Equal(later, Assert.Single(history.Load()).FinishedAt);
        Assert.Equal("", vm.OvertimeHistoryError);
    }

    [Fact]
    public void FailedHistoryWriteIsVisibleAndRefreshRetriesWithoutDoubleCounting()
    {
        DateTime now = new(2026, 10, 23, 20, 0, 0);
        var state = new StateStore();
        var history = new FailingHistory();
        using var vm = new WorkCountdownViewModel(() => now, state, false, history);
        vm.StartTimeText = "09:30";
        vm.StartCommand.Execute(null);
        history.FailWrite = true;
        vm.FinishCommand.Execute(null);
        Assert.True(vm.IsFinished);
        Assert.Equal(now, state.State!.FinishedAt);
        Assert.Contains("尚未保存", vm.OvertimeHistoryError);
        Assert.Empty(vm.OvertimeRecords);
        vm.ResetCommand.Execute(null);
        history.FailWrite = false;
        vm.RefreshOvertimeHistoryCommand.Execute(null);
        vm.RefreshOvertimeHistoryCommand.Execute(null);
        Assert.Single(vm.OvertimeRecords);
        Assert.Equal("", vm.OvertimeHistoryError);
        Assert.Equal(1, history.SuccessfulWrites);
    }

    [Fact]
    public void RestoringStaleCountdownDoesNotReplaceALaterHistoryEntry()
    {
        var history = new MemoryOvertimeHistoryStore();
        DateTime start = new(2026, 10, 23, 9, 30, 0), normal = start.AddHours(9), later = start.Date.AddHours(21);
        history.Upsert(new(new(2026, 10, 23), start, normal, later, TimeSpan.FromHours(2).Ticks));
        var state = new StateStore { State = new(new(2026, 10, 23), "09:30", CountdownDayMode.Workday, 0, start.Date.AddHours(20)) };
        using var vm = new WorkCountdownViewModel(() => later.AddDays(1), state, false, history);
        Assert.Equal(later, Assert.Single(history.Load()).FinishedAt);
    }

    [Fact]
    public void LateArrivalAfterNormalEndDoesNotCountTimeBeforeArrival()
    {
        DateTime now = new(2026, 10, 23, 20, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new StateStore(), false);
        vm.StartTimeText = "19:30";
        vm.StartCommand.Execute(null);
        vm.FinishCommand.Execute(null);
        Assert.Contains("0小时30分钟", Assert.Single(vm.OvertimeRecords).DurationText);
        Assert.Equal("", vm.OvertimeHistoryError);
    }

    [Theory]
    [InlineData(18, 0, "0小时")]
    [InlineData(19, 15, "30分钟")]
    [InlineData(21, 0, "2小时")]
    public void FinishRecordsNetActualOvertimeNotTheDraftOrPlannedHours(int hour, int minute, string expected)
    {
        DateTime now = new(2026, 10, 23, 10, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new StateStore(), false);
        vm.DayMode = CountdownDayMode.Workday;
        vm.StartTimeText = "09:30";
        vm.OvertimeText = "2";
        vm.StartCommand.Execute(null);
        vm.StartTimeText = "07:30";
        vm.OvertimeText = "8";
        vm.DayMode = CountdownDayMode.RestDay;
        now = new(2026, 10, 23, hour, minute, 0);
        vm.FinishCommand.Execute(null);
        var row = Assert.Single(Rows(vm));
        Assert.Contains(expected, Get<string>(row, "DurationText"));
        Assert.Equal("09:30", Get<string>(row, "StartedAtText"));
        Assert.Equal("2026-10-23", Get<string>(row, "DateText"));
        Assert.Equal("工作日", Get<string>(row, "DayTypeText"));
        Assert.Equal("", Get<string>(vm, "OvertimeHistoryError"));
    }

    [Fact]
    public void RestDayCountsAllActualWorkMinusLunchAndDinnerIncludingUnplannedOverrun()
    {
        DateTime now = new(2026, 10, 24, 9, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new StateStore(), false);
        vm.StartTimeText = "08:00";
        vm.OvertimeText = "4";
        vm.StartCommand.Execute(null);
        now = now.Date.AddHours(20);
        vm.FinishCommand.Execute(null);
        var row = Assert.Single(Rows(vm));
        Assert.Contains("10小时30分钟", Get<string>(row, "DurationText"));
        Assert.Equal("休息日", Get<string>(row, "DayTypeText"));
        Assert.Contains("10小时30分钟", Get<string>(vm, "OvertimeSummaryText"));
    }

    [Fact]
    public void SameDateRestartReplacesAndResetDoesNotDeleteHistory()
    {
        DateTime now = new(2026, 10, 23, 19, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new StateStore(), false);
        vm.StartTimeText = "09:30";
        vm.StartCommand.Execute(null);
        vm.FinishCommand.Execute(null);
        vm.FinishCommand.Execute(null);
        Assert.Single(Rows(vm));
        now = now.Date.AddHours(21);
        vm.StartCommand.Execute(null);
        vm.FinishCommand.Execute(null);
        Assert.Contains("2小时", Get<string>(Assert.Single(Rows(vm)), "DurationText"));
        vm.ResetCommand.Execute(null);
        Assert.Single(Rows(vm));
    }

    [Fact]
    public void NewMonthStartsAtZeroAndPreviousMonthRemainsBrowsable()
    {
        DateTime now = new(2026, 10, 30, 19, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new StateStore(), false);
        vm.StartTimeText = "09:30";
        vm.StartCommand.Execute(null);
        vm.FinishCommand.Execute(null);
        Assert.Single(Rows(vm));
        now = new(2026, 11, 1, 9, 0, 0);
        vm.Refresh();
        Assert.Equal("2026年11月", Get<string>(vm, "OvertimeMonthText"));
        Assert.Empty(Rows(vm));
        Assert.Contains("0小时", Get<string>(vm, "OvertimeSummaryText"));
        Get<RelayCommand>(vm, "PreviousOvertimeMonthCommand").Execute(null);
        Assert.Equal("2026年10月", Get<string>(vm, "OvertimeMonthText"));
        Assert.Single(Rows(vm));
        vm.Refresh();
        Assert.Single(Rows(vm));
        Get<RelayCommand>(vm, "CurrentOvertimeMonthCommand").Execute(null);
        Assert.Empty(Rows(vm));
    }

    [Fact]
    public void JsonStateUsesSiblingHistoryAndRecoversThePreviousDaysFinish()
    {
        string root = Path.Combine(Path.GetTempPath(), "ToolsBox-overtime-vm-" + Guid.NewGuid().ToString("N"));
        try
        {
            var stateStore = new JsonCountdownStateStore(Path.Combine(root, "state.json"));
            stateStore.Save(new(new(2026, 10, 23), "09:30", CountdownDayMode.Workday, 0,
                new DateTime(2026, 10, 23, 20, 0, 0)));
            using (var vm = new WorkCountdownViewModel(() => new(2026, 10, 24, 9, 0, 0), stateStore, false))
            {
                Assert.Null(vm.Schedule);
                Assert.Contains("1小时", Get<string>(Assert.Single(Rows(vm)), "DurationText"));
            }
            Assert.True(File.Exists(Path.Combine(root, "overtime-history.json")));
            using var reopened = new WorkCountdownViewModel(() => new(2026, 10, 24, 9, 0, 0), stateStore, false);
            Assert.Single(Rows(reopened));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void CrossMonthOvernightFinishBelongsToItsWorkDateAndKeepsSeconds()
    {
        DateTime now = new(2026, 10, 31, 9, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new StateStore(), false);
        vm.StartTimeText = "08:00";
        vm.OvertimeText = "4";
        vm.StartCommand.Execute(null);
        now = new(2026, 11, 1, 0, 0, 30);
        vm.FinishCommand.Execute(null);
        Assert.Empty(Rows(vm));
        Get<RelayCommand>(vm, "PreviousOvertimeMonthCommand").Execute(null);
        var row = Assert.Single(Rows(vm));
        Assert.Equal("2026-10-31", Get<string>(row, "DateText"));
        Assert.Equal("11-01 00:00", Get<string>(row, "FinishedAtText"));
        Assert.Contains("14小时30分钟30秒", Get<string>(row, "DurationText"));
    }

    private static object[] Rows(object vm) => Get<IEnumerable>(vm, "OvertimeRecords").Cast<object>().ToArray();
    private static T Get<T>(object target, string name)
    {
        var property = target.GetType().GetProperty(name);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<T>(property!.GetValue(target));
    }

    private sealed class StateStore : ICountdownStateStore
    {
        public CountdownState? State;
        public CountdownState? Load() => State;
        public void Save(CountdownState state) => State = state;
        public void Clear() => State = null;
    }

    private sealed class FailingHistory : IOvertimeHistoryStore
    {
        private readonly MemoryOvertimeHistoryStore _inner = new();
        public bool FailWrite;
        public bool FailRead;
        public int SuccessfulWrites;
        public IReadOnlyList<OvertimeRecord> Load() => FailRead ? throw new IOException("synthetic read failure") : _inner.Load();
        public void Upsert(OvertimeRecord record)
        {
            if (FailWrite || FailRead) throw new IOException("synthetic write failure");
            _inner.Upsert(record);
            SuccessfulWrites++;
        }
    }
}
