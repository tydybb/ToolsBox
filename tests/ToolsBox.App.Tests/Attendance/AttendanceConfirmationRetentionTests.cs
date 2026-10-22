using System.IO;
using ToolsBox.App.Attendance;
using ToolsBox.App.WorkCountdown;
using ToolsBox.Core.Attendance;

namespace ToolsBox.App.Tests.Attendance;

public sealed class AttendanceConfirmationRetentionTests
{
    [Theory]
    [InlineData("failure")]
    [InlineData("exception")]
    [InlineData("no-card")]
    [InlineData("invalid-time")]
    [InlineData("future-time")]
    public async Task UnconfirmedManualRereadRetainsPreviouslyConfirmedTime(string outcome)
    {
        using var test = new TestStore();
        DateTime at = test.Now.Date.AddHours(7).AddMinutes(50);
        test.Store.SaveSnapshot(new(test.Now, at, TestStore.Account, "earlier confirmation"));
        test.RequestCheck();
        var coordinator = test.Coordinator((_, _) => ReadOutcome(outcome));
        int alerts = 0;
        coordinator.ReminderDue += (_, _) => alerts++;

        await coordinator.CheckAsync(true, true);

        var snapshot = test.Store.LoadSnapshot()!;
        Assert.Equal(at, snapshot.ClockInAt);
        Assert.True(AttendanceCoordinator.IsConfirmed(snapshot, test.Store.LoadOptions(), test.Now));
        Assert.Equal(test.Now, snapshot.CompletedRequest);
        Assert.Contains("本次读取", snapshot.Message);
        Assert.Contains("保留", snapshot.Message);
        Assert.Contains("07:50", snapshot.Message);
        Assert.DoesNotContain("已读取本地上班打卡", snapshot.Message);
        Assert.Equal(0, alerts);
    }

    [Theory]
    [InlineData("other-account")]
    [InlineData("yesterday-clock-in")]
    [InlineData("future-clock-in")]
    [InlineData("yesterday-check")]
    [InlineData("tomorrow-check")]
    [InlineData("future-check")]
    public async Task InvalidSavedConfirmationIsNotReused(string invalid)
    {
        using var test = new TestStore();
        DateTime at = test.Now.Date.AddHours(7).AddMinutes(50);
        DateTime checkedAt = test.Now;
        string account = TestStore.Account;
        switch (invalid)
        {
            case "other-account": account = "another-synthetic-account"; break;
            case "yesterday-clock-in": at = at.AddDays(-1); break;
            case "future-clock-in": at = test.Now.AddMinutes(1); break;
            case "yesterday-check": checkedAt = checkedAt.AddDays(-1); break;
            case "tomorrow-check": checkedAt = checkedAt.AddDays(1); break;
            case "future-check": checkedAt = checkedAt.AddMinutes(1); break;
        }
        test.Store.SaveSnapshot(new(checkedAt, at, account, "invalid saved confirmation"));
        test.RequestCheck();
        var coordinator = test.Coordinator((_, _) => ReadOutcome("failure"));

        await coordinator.CheckAsync(true, true);

        var snapshot = test.Store.LoadSnapshot()!;
        Assert.Null(snapshot.ClockInAt);
        Assert.DoesNotContain("保留", snapshot.Message);
        Assert.False(AttendanceCoordinator.IsConfirmed(snapshot, test.Store.LoadOptions(), test.Now));
    }

    [Fact]
    public async Task FreshConfirmedReadingSupersedesEarlierConfirmation()
    {
        using var test = new TestStore();
        test.Store.SaveSnapshot(new(test.Now, test.Now.Date.AddHours(7).AddMinutes(50), TestStore.Account, "earlier confirmation"));
        test.RequestCheck();
        var coordinator = test.Coordinator((_, _) => Task.FromResult(new AttendanceStatus(true, true, "07:55", null, 1, null)));

        await coordinator.CheckAsync(true, true);

        var snapshot = test.Store.LoadSnapshot()!;
        Assert.Equal(test.Now.Date.AddHours(7).AddMinutes(55), snapshot.ClockInAt);
        Assert.Contains("已读取本地上班打卡：07:55", snapshot.Message);
        Assert.DoesNotContain("保留", snapshot.Message);
    }

    [Fact]
    public async Task PendingManualRereadRetainsLatestConcurrentConfirmation()
    {
        using var test = new TestStore();
        test.Store.SaveSnapshot(new(test.Now, test.Now.Date.AddHours(7).AddMinutes(50), TestStore.Account, "initial confirmation"));
        test.RequestCheck();
        var pending = new TaskCompletionSource<AttendanceStatus>();
        var coordinator = test.Coordinator((_, _) => pending.Task);
        Task check = coordinator.CheckAsync(true, true);
        test.Now = test.Now.AddSeconds(1);
        DateTime latest = test.Now.Date.AddHours(7).AddMinutes(55);
        test.Store.SaveSnapshot(new(test.Now, latest, TestStore.Account, "concurrent preview confirmation"));

        pending.SetResult(AttendanceStatus.Failure("synthetic unavailable"));
        await check;

        var snapshot = test.Store.LoadSnapshot()!;
        Assert.Equal(latest, snapshot.ClockInAt);
        Assert.Contains("07:55", snapshot.Message);
        Assert.Contains("保留", snapshot.Message);
    }

    [Fact]
    public async Task PendingAutomaticFailureKeepsConcurrentConfirmationAndDoesNotRemind()
    {
        using var test = new TestStore(new DateTime(2026, 9, 22, 9, 24, 59));
        var pending = new TaskCompletionSource<AttendanceStatus>();
        var coordinator = test.Coordinator((_, _) => pending.Task);
        int alerts = 0;
        coordinator.ReminderDue += (_, _) => alerts++;
        Task check = coordinator.CheckAsync(true, true);
        test.Now = test.Now.AddSeconds(1);
        DateTime at = test.Now.Date.AddHours(8);
        test.Store.SaveSnapshot(new(test.Now, at, TestStore.Account, "concurrent preview confirmation"));

        pending.SetResult(AttendanceStatus.Failure("synthetic unavailable"));
        await check;

        Assert.Equal(at, test.Store.LoadSnapshot()!.ClockInAt);
        Assert.Equal(0, alerts);
        Assert.Null(test.Store.LoadSnapshot()!.LastReminder);
        Assert.Null(test.Store.LoadSnapshot()!.FinalReminderDate);
    }

    [Fact]
    public async Task BusyFinalReminderDoesNotRevokeConfirmedManualReread()
    {
        using var test = new TestStore(new DateTime(2026, 9, 22, 9, 24, 59));
        DateTime at = test.Now.Date.AddHours(8);
        test.Store.SaveSnapshot(new(test.Now, at, TestStore.Account, "earlier confirmation"));
        test.RequestCheck();
        var pending = new TaskCompletionSource<AttendanceStatus>();
        var coordinator = test.Coordinator((_, _) => pending.Task);
        int alerts = 0;
        coordinator.ReminderDue += (_, _) => alerts++;
        Task check = coordinator.CheckAsync(true, true);
        test.Now = test.Now.AddSeconds(1);

        await coordinator.CheckAsync(true);
        var whilePending = test.Store.LoadSnapshot();
        int alertsWhilePending = alerts;
        pending.SetResult(AttendanceStatus.Failure("synthetic unavailable"));
        await check;

        Assert.Equal(at, whilePending!.ClockInAt);
        Assert.Equal(0, alertsWhilePending);
        Assert.Equal(at, test.Store.LoadSnapshot()!.ClockInAt);
        Assert.Equal(0, alerts);
        Assert.Null(test.Store.LoadSnapshot()!.LastReminder);
        Assert.Null(test.Store.LoadSnapshot()!.FinalReminderDate);
    }

    [Fact]
    public async Task RetainedTimeDoesNotReimportAlreadyHandledCountdown()
    {
        using var test = new TestStore();
        DateTime at = test.Now.Date.AddHours(7).AddMinutes(50);
        test.Store.SaveSnapshot(new(test.Now, at, TestStore.Account, "earlier confirmation"));
        test.Store.Update(o => o with { HandledClockIn = at });
        test.RequestCheck();
        var coordinator = test.Coordinator((_, _) => ReadOutcome("failure"));
        var countdownStore = new CountdownMemory();
        using var countdown = new WorkCountdownViewModel(() => test.Now, countdownStore, false);
        int confirmations = 0;
        using var bridge = new AttendanceCountdownBridge(test.Store, countdown, _ => { confirmations++; return true; }, false, () => test.Now, () => true);

        await coordinator.CheckAsync(true, true);
        bridge.Tick();
        bridge.Tick();

        Assert.Equal(at, test.Store.LoadSnapshot()!.ClockInAt);
        Assert.Equal(at, test.Store.LoadOptions().HandledClockIn);
        Assert.Null(countdown.Schedule);
        Assert.Equal(0, countdownStore.Saves);
        Assert.Equal(0, confirmations);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("account-changed")]
    [InlineData("next-day")]
    public async Task InvalidatedPendingRequestLeavesOriginalSnapshotUntouched(string change)
    {
        using var test = new TestStore();
        var previous = new AttendanceSnapshot(test.Now, test.Now.Date.AddHours(7).AddMinutes(50), TestStore.Account, "earlier confirmation");
        test.Store.SaveSnapshot(previous);
        test.RequestCheck();
        var pending = new TaskCompletionSource<AttendanceStatus>();
        var coordinator = test.Coordinator((_, _) => pending.Task);
        Task check = coordinator.CheckAsync(true, true);
        switch (change)
        {
            case "disabled": test.Store.Update(o => o with { Enabled = false }); break;
            case "account-changed": test.Store.Update(o => o with { AccountDirectory = "another-synthetic-account" }); break;
            case "next-day": test.Now = test.Now.AddDays(1); break;
        }

        pending.SetResult(AttendanceStatus.Failure("synthetic unavailable"));
        await check;

        Assert.Equal(previous, test.Store.LoadSnapshot());
    }

    [Fact]
    public async Task ManualConfirmationDoesNotCreateClockInTime()
    {
        using var test = new TestStore();
        test.Store.Update(o => o with { ManualConfirmedOn = DateOnly.FromDateTime(test.Now) });
        test.RequestCheck();
        var coordinator = test.Coordinator((_, _) => ReadOutcome("failure"));

        await coordinator.CheckAsync(true, true);

        Assert.Null(test.Store.LoadSnapshot()!.ClockInAt);
    }

    private static Task<AttendanceStatus> ReadOutcome(string outcome) => outcome switch
    {
        "exception" => throw new InvalidOperationException("synthetic read error"),
        "no-card" => Task.FromResult(new AttendanceStatus(true, false, null, null, 1, null)),
        "invalid-time" => Task.FromResult(new AttendanceStatus(true, true, "not-a-time", null, 1, null)),
        "future-time" => Task.FromResult(new AttendanceStatus(true, true, "09:00", null, 1, null)),
        _ => Task.FromResult(AttendanceStatus.Failure("synthetic unavailable"))
    };

    private sealed class TestStore : IDisposable
    {
        public const string Account = "synthetic-account";
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ToolsBox-attendance-retention-" + Guid.NewGuid().ToString("N"));
        public AttendanceStore Store { get; }
        public DateTime Now { get; set; }

        public TestStore(DateTime? now = null)
        {
            Now = now ?? new DateTime(2026, 9, 22, 8, 0, 0);
            Store = new AttendanceStore(_root);
            Store.Update(o => o with { Enabled = true, AccountDirectory = Account });
        }

        public void RequestCheck() => Store.Update(o => o with { CheckRequestedAt = Now });
        public AttendanceCoordinator Coordinator(Func<AttendanceOptions, DateTime, Task<AttendanceStatus>> read) => new(Store, read, () => Now);
        public void Dispose() => Directory.Delete(_root, true);
    }

    private sealed class CountdownMemory : ICountdownStateStore
    {
        public int Saves { get; private set; }
        public CountdownState? Load() => null;
        public void Save(CountdownState value) => Saves++;
        public void Clear() { }
    }
}
