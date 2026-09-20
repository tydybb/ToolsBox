using System.IO;
using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class WorkCountdownViewModelTests
{
    [Theory]
    [InlineData(CountdownDayMode.Workday, 2, "加班小副本")]
    [InlineData(CountdownDayMode.Workday, 3, "加班小副本")]
    [InlineData(CountdownDayMode.Workday, 4, "续航模式")]
    [InlineData(CountdownDayMode.Workday, 5, "续航模式")]
    [InlineData(CountdownDayMode.Workday, 6, "加长版")]
    [InlineData(CountdownDayMode.Workday, 8, "加长版")]
    [InlineData(CountdownDayMode.Workday, 9, "身体不是无限续航")]
    [InlineData(CountdownDayMode.Workday, 14, "身体不是无限续航")]
    [InlineData(CountdownDayMode.Workday, 15, "别吹牛逼")]
    [InlineData(CountdownDayMode.Workday, 100, "别吹牛逼")]
    [InlineData(CountdownDayMode.RestDay, 4, "续航模式")]
    [InlineData(CountdownDayMode.RestDay, 8, "加长版")]
    [InlineData(CountdownDayMode.RestDay, 15, "身体不是无限续航")]
    [InlineData(CountdownDayMode.RestDay, 21, "身体不是无限续航")]
    [InlineData(CountdownDayMode.RestDay, 22, "别吹牛逼")]
    [InlineData(CountdownDayMode.RestDay, 100, "别吹牛逼")]
    public void OvertimeCard_UsesDurationTiers_WithoutBlockingLongTasks(CountdownDayMode mode, int hours, string message)
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 9, 30, 0), new MemoryStore(), false);
        vm.StartTimeText = "0930";
        vm.DayMode = mode;
        vm.OvertimeText = hours.ToString(System.Globalization.CultureInfo.InvariantCulture);
        vm.StartCommand.Execute(null);

        Assert.NotNull(vm.Schedule);
        Assert.Empty(vm.ErrorMessage);
        Assert.True(vm.HasOvertime);
        Assert.Equal($"计划加班：{hours} 小时（不含休息）", vm.OvertimeDurationText);
        Assert.Contains(message, vm.OvertimeMessage);
    }

    [Theory]
    [InlineData(CountdownDayMode.Workday, 2, "加班时段：2026-09-18 18:30 — 2026-09-18 21:00")]
    [InlineData(CountdownDayMode.Workday, 6, "加班时段：2026-09-18 18:30 — 2026-09-19 01:00")]
    [InlineData(CountdownDayMode.RestDay, 4, "加班时段：2026-09-18 09:30 — 2026-09-18 14:30")]
    public void OvertimeCard_ShowsPlannedInterval_IncludingDatesAndMealBreaks(CountdownDayMode mode, int hours, string expected)
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 9, 30, 0), new MemoryStore(), false);
        vm.StartTimeText = "0930";
        vm.DayMode = mode;
        vm.OvertimeText = hours.ToString(System.Globalization.CultureInfo.InvariantCulture);
        vm.StartCommand.Execute(null);
        Assert.Equal(expected, vm.OvertimePeriodText);
    }

    [Fact]
    public void OvertimeCard_StaysWithActiveTask_AndHidesOnResetOrNoOvertime()
    {
        DateTime now = new(2026, 9, 18, 9, 30, 0);
        using var vm = new WorkCountdownViewModel(() => now, new MemoryStore(), false);
        Assert.False(vm.HasOvertime);
        vm.StartCommand.Execute(null);
        Assert.False(vm.HasOvertime);
        Assert.Empty(vm.OvertimeDurationText);
        Assert.Empty(vm.OvertimePeriodText);
        Assert.Empty(vm.OvertimeMessage);

        vm.OvertimeText = "2";
        vm.StartCommand.Execute(null);
        Assert.True(vm.HasOvertime);
        var duration = vm.OvertimeDurationText;
        var period = vm.OvertimePeriodText;
        var message = vm.OvertimeMessage;
        vm.DayMode = CountdownDayMode.RestDay;
        vm.OvertimeText = "22";
        Assert.Equal(duration, vm.OvertimeDurationText);
        Assert.Equal(period, vm.OvertimePeriodText);
        Assert.Equal(message, vm.OvertimeMessage);

        vm.OvertimeText = "1";
        vm.StartCommand.Execute(null);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Equal(message, vm.OvertimeMessage);
        now = now.AddDays(1);
        vm.Refresh();
        Assert.Equal(period, vm.OvertimePeriodText);
        Assert.Equal(message, vm.OvertimeMessage);

        vm.ResetCommand.Execute(null);
        Assert.False(vm.HasOvertime);
        Assert.Empty(vm.OvertimeDurationText);
        Assert.Empty(vm.OvertimePeriodText);
        Assert.Empty(vm.OvertimeMessage);
    }

    [Theory]
    [InlineData(CountdownDayMode.Workday, 15, "别吹牛逼")]
    [InlineData(CountdownDayMode.RestDay, 15, "身体不是无限续航")]
    public void OvertimeCard_RestoresSavedTaskClassification(CountdownDayMode mode, int hours, string expected)
    {
        var store = new MemoryStore { State = new CountdownState(new(2026, 9, 18), "09:30", mode, hours) };
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 12, 0, 0), store, false);
        Assert.True(vm.HasOvertime);
        Assert.Contains(expected, vm.OvertimeMessage);
        Assert.Contains("15 小时", vm.OvertimeDurationText);
    }

    [Theory]
    [InlineData("08:30")]
    [InlineData("08：30")]
    [InlineData("0830")]
    [InlineData("8:30")]
    [InlineData("8：30")]
    [InlineData(" 0830 ")]
    public void Start_AcceptsTimeFormats_AndSavesCanonicalTime(string input)
    {
        var store = new MemoryStore();
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 9, 0, 0), store, false);
        vm.StartTimeText = input;
        vm.StartCommand.Execute(null);

        Assert.NotNull(vm.Schedule);
        Assert.Equal(new DateTime(2026, 9, 18, 8, 30, 0), vm.Schedule.Start);
        Assert.Equal(new DateTime(2026, 9, 18, 17, 30, 0), vm.Schedule.End);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal("08:30", vm.StartTimeText);
        Assert.Equal("08:30", store.State!.StartTime);
        Assert.False(vm.HasPendingChanges);

        using var restored = new WorkCountdownViewModel(() => new(2026, 9, 18, 12, 0, 0), store, false);
        Assert.Equal(vm.Schedule, restored.Schedule);
        Assert.Equal("08:30", restored.StartTimeText);
        Assert.False(restored.HasPendingChanges);
    }

    [Theory]
    [InlineData("0730", 16, 30, false)]
    [InlineData("07：30", 16, 30, false)]
    [InlineData("0930", 18, 30, false)]
    [InlineData("09：30", 18, 30, false)]
    [InlineData("0931", 18, 30, true)]
    [InlineData("09：31", 18, 30, true)]
    public void NewTimeFormats_PreserveAttendanceRules(string input, int endHour, int endMinute, bool late)
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 9, 0, 0), new MemoryStore(), false);
        vm.StartTimeText = input;
        vm.StartCommand.Execute(null);

        Assert.NotNull(vm.Schedule);
        Assert.Equal(new DateTime(2026, 9, 18, endHour, endMinute, 0), vm.Schedule.End);
        Assert.Equal(late, vm.Schedule.IsLate);
    }

    [Theory]
    [InlineData("0729")]
    [InlineData("07：29")]
    [InlineData("830")]
    [InlineData("08300")]
    [InlineData("2400")]
    [InlineData("24：00")]
    [InlineData("2360")]
    [InlineData("23：60")]
    [InlineData("8:3")]
    [InlineData("8：3")]
    [InlineData("08;30")]
    [InlineData("08::30")]
    [InlineData("08：:30")]
    [InlineData("abcd")]
    [InlineData("")]
    public void InvalidTime_DoesNotReplaceOrSaveActiveTask(string input)
    {
        var store = new MemoryStore();
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 9, 0, 0), store, false);
        vm.StartCommand.Execute(null);
        var original = vm.Schedule;
        var saved = store.State;

        vm.StartTimeText = input;
        vm.StartCommand.Execute(null);

        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Same(original, vm.Schedule);
        Assert.Same(saved, store.State);
        Assert.Equal(input, vm.StartTimeText);
        Assert.True(vm.HasPendingChanges);
    }

    [Fact]
    public void Start_UsesTargetTime_AndJumpingClockCatchesUp()
    {
        DateTime now = new(2026, 9, 18, 9, 30, 0);
        var store = new MemoryStore();
        using var vm = new WorkCountdownViewModel(() => now, store, false);
        vm.StartTimeText = "09:30";
        vm.OvertimeText = "2";
        vm.StartCommand.Execute(null);
        Assert.Equal(new DateTime(2026, 9, 18, 21, 0, 0), vm.Schedule!.End);
        Assert.False(vm.Schedule.IsLate);
        Assert.Equal("11:30:00", vm.RemainingText);
        now = new(2026, 9, 18, 19, 15, 0);
        vm.Refresh();
        Assert.Equal("01:45:00", vm.RemainingText);
        now = new(2026, 9, 18, 22, 0, 0);
        vm.Refresh();
        Assert.Equal("00:00:00", vm.RemainingText);
        Assert.Equal("你已无偿加班", vm.StatusText);
        Assert.NotNull(store.State);
    }

    [Fact]
    public void LateStart_Keeps1830_AndInvalidEditsDoNotReplaceActiveTask()
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 10, 0, 0), new MemoryStore(), false);
        vm.StartTimeText = "09:31";
        vm.StartCommand.Execute(null);
        var original = vm.Schedule;
        Assert.True(original!.IsLate);
        Assert.Equal(new DateTime(2026, 9, 18, 18, 30, 0), original.End);
        Assert.Equal("今日迟到", vm.AttendanceText);
        vm.StartTimeText = "07:29";
        Assert.True(vm.HasPendingChanges);
        vm.StartCommand.Execute(null);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Same(original, vm.Schedule);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2.5")]
    [InlineData("-2")]
    [InlineData("abc")]
    [InlineData("2147483648")]
    public void InvalidOvertime_DoesNotStart(string hours)
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 8, 0, 0), new MemoryStore(), false);
        vm.OvertimeText = hours;
        vm.StartCommand.Execute(null);
        Assert.Null(vm.Schedule);
        Assert.NotEmpty(vm.ErrorMessage);
    }

    [Fact]
    public void UnknownCalendar_RequiresOverride_AndRestDaySkipsLunch()
    {
        using var vm = new WorkCountdownViewModel(() => new(2027, 1, 2, 9, 30, 0), new MemoryStore(), false);
        vm.StartTimeText = "09:30";
        vm.OvertimeText = "4";
        vm.StartCommand.Execute(null);
        Assert.Null(vm.Schedule);
        Assert.Contains("日历", vm.ErrorMessage);
        vm.DayMode = CountdownDayMode.RestDay;
        vm.StartCommand.Execute(null);
        Assert.Equal(new DateTime(2027, 1, 2, 14, 30, 0), vm.Schedule!.End);
        Assert.Equal("全天加班", vm.AttendanceText);
    }

    [Fact]
    public void CurrentTime_FillsMinutes_AndEarlyTimeCannotStartEvenOnWeekend()
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 19, 7, 29, 59), new MemoryStore(), false);
        vm.UseCurrentTimeCommand.Execute(null);
        Assert.Equal("07:29", vm.StartTimeText);
        Assert.Null(vm.Schedule);
        vm.OvertimeText = "4";
        vm.StartCommand.Execute(null);
        Assert.Null(vm.Schedule);
        Assert.NotEmpty(vm.ErrorMessage);
    }

    [Fact]
    public void SameDayRestores_OldDayDoesNot_AndResetClears()
    {
        var store = new MemoryStore();
        using (var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 9, 0, 0), store, false))
        {
            vm.StartTimeText = "08:00";
            vm.StartCommand.Execute(null);
        }
        using var restored = new WorkCountdownViewModel(() => new(2026, 9, 18, 12, 0, 0), store, false);
        Assert.Equal("05:00:00", restored.RemainingText);
        using var tomorrow = new WorkCountdownViewModel(() => new(2026, 9, 19, 8, 0, 0), store, false);
        Assert.Null(tomorrow.Schedule);
        restored.ResetCommand.Execute(null);
        Assert.Null(restored.Schedule);
        Assert.Null(store.State);
    }

    [Fact]
    public void SaveFailure_IsVisible_ButCountdownStillWorks()
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 9, 0, 0), new MemoryStore { FailSave = true }, false);
        vm.StartCommand.Execute(null);
        Assert.NotNull(vm.Schedule);
        Assert.Contains("保存", vm.ErrorMessage);
    }

    [Fact]
    public void AcrossMidnight_ActiveTargetIsStable_NewStartUsesToday()
    {
        DateTime now = new(2026, 9, 18, 9, 30, 0);
        using var vm = new WorkCountdownViewModel(() => now, new MemoryStore(), false);
        vm.StartTimeText = "09:30";
        vm.OvertimeText = "6";
        vm.StartCommand.Execute(null);
        var target = vm.Schedule!.End;
        now = new(2026, 9, 19, 0, 30, 0);
        vm.Refresh();
        Assert.Equal(target, vm.Schedule.End);
        Assert.Equal("00:30:00", vm.RemainingText);
        Assert.True(vm.HasPendingChanges);
    }

    [Fact]
    public void DisposedModel_CannotStartOrReset()
    {
        var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 9, 0, 0), new MemoryStore(), false);
        vm.Dispose();
        Assert.False(vm.StartCommand.CanExecute(null));
        vm.StartCommand.Execute(null);
        Assert.Null(vm.Schedule);
    }

    private sealed class MemoryStore : ICountdownStateStore
    {
        public CountdownState? State { get; set; }
        public bool FailSave { get; init; }
        public CountdownState? Load() => State;
        public void Save(CountdownState state)
        {
            if (FailSave) throw new IOException("test");
            State = state;
        }
        public void Clear() => State = null;
    }
}
