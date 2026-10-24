using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using ToolsBox.App.Infrastructure;
using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.App.WorkCountdown;

public sealed record OvertimeRecordRow(OvertimeRecord Record)
{
    public string DateText => Record.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string DayTypeText => Record.NormalEnd.HasValue ? "工作日" : "休息日";
    public string StartedAtText => Record.StartedAt.ToString("HH:mm", CultureInfo.InvariantCulture);
    public string FinishedAtText => Record.FinishedAt.ToString(
        Record.FinishedAt.Date == Record.StartedAt.Date ? "HH:mm" : "MM-dd HH:mm", CultureInfo.InvariantCulture);
    public string DurationText => WorkCountdownViewModel.FormatOvertime(Record.OvertimeTicks);
}

public sealed partial class WorkCountdownViewModel
{
    private readonly IOvertimeHistoryStore _historyStore;
    private IReadOnlyList<OvertimeRecord> _allOvertimeRecords = [];
    private readonly Dictionary<DateOnly, (OvertimeRecord Record, bool IsRecovery)> _pendingOvertime = [];
    private DateTime _selectedOvertimeMonth;
    private bool _followCurrentOvertimeMonth = true;
    private string _overtimeHistoryError = "";
    private string _overtimeSummary = "";
    private DispatcherTimer? _overtimeMonthTimer;

    public ObservableCollection<OvertimeRecordRow> OvertimeRecords { get; } = [];
    public string OvertimeMonthText => _selectedOvertimeMonth.ToString("yyyy年MM月", CultureInfo.InvariantCulture);
    public string OvertimeSummaryText => _overtimeSummary;
    public string OvertimeHistoryError { get => _overtimeHistoryError; private set => SetProperty(ref _overtimeHistoryError, value); }
    public bool HasOvertimeRecords => OvertimeRecords.Count > 0;
    public bool HasNoOvertimeRecords => !HasOvertimeRecords;
    public RelayCommand PreviousOvertimeMonthCommand { get; private set; } = null!;
    public RelayCommand NextOvertimeMonthCommand { get; private set; } = null!;
    public RelayCommand CurrentOvertimeMonthCommand { get; private set; } = null!;
    public RelayCommand RefreshOvertimeHistoryCommand { get; private set; } = null!;

    private static IOvertimeHistoryStore DefaultHistoryStore(ICountdownStateStore stateStore) => stateStore is JsonCountdownStateStore json
        ? new JsonOvertimeHistoryStore(Path.Combine(Path.GetDirectoryName(json.FilePath)!, "overtime-history.json"))
        : new MemoryOvertimeHistoryStore();

    private void InitializeOvertimeHistory()
    {
        _selectedOvertimeMonth = MonthOf(_now());
        PreviousOvertimeMonthCommand = new RelayCommand(() => SelectOvertimeMonth(-1),
            () => !_disposed && _selectedOvertimeMonth > DateTime.MinValue);
        NextOvertimeMonthCommand = new RelayCommand(() => SelectOvertimeMonth(1),
            () => !_disposed && _selectedOvertimeMonth < new DateTime(9999, 12, 1));
        CurrentOvertimeMonthCommand = new RelayCommand(() =>
        {
            if (_disposed) return;
            _followCurrentOvertimeMonth = true;
            _selectedOvertimeMonth = MonthOf(_now());
            RefreshOvertimeHistory();
        }, () => !_disposed);
        RefreshOvertimeHistoryCommand = new RelayCommand(RefreshOvertimeHistory, () => !_disposed);
        RefreshOvertimeHistory();
    }

    private void SelectOvertimeMonth(int offset)
    {
        if (_disposed || offset < 0 && _selectedOvertimeMonth == DateTime.MinValue
            || offset > 0 && _selectedOvertimeMonth == new DateTime(9999, 12, 1)) return;
        _followCurrentOvertimeMonth = false;
        _selectedOvertimeMonth = _selectedOvertimeMonth.AddMonths(offset);
        RefreshOvertimeHistory();
    }

    private void RefreshOvertimeHistory()
    {
        if (_disposed) return;
        if (_followCurrentOvertimeMonth) _selectedOvertimeMonth = MonthOf(_now());
        bool saved = PersistPendingOvertime();
        try
        {
            _allOvertimeRecords = _historyStore.Load();
            if (saved) OvertimeHistoryError = "";
        }
        catch (Exception error) when (IsStorageError(error))
        {
            OvertimeHistoryError = "无法读取加班记录；请检查文件或权限后点击“刷新”重试，原文件未被清空。";
        }
        ShowOvertimeMonth();
    }

    private bool PersistPendingOvertime()
    {
        foreach (var pair in _pendingOvertime.ToArray())
        {
            try
            {
                if (pair.Value.IsRecovery) _historyStore.UpsertIfNewer(pair.Value.Record);
                else _historyStore.Upsert(pair.Value.Record);
                _pendingOvertime.Remove(pair.Key);
            }
            catch (Exception error) when (IsStorageError(error))
            {
                OvertimeHistoryError = "已下班，但加班记录尚未保存；请检查文件或权限后点击“刷新”重试。";
                return false;
            }
        }
        return true;
    }

    private static OvertimeRecord MakeOvertimeRecord(CountdownState state, WorkSchedule schedule)
    {
        DateTime overtimeStart = schedule.NormalEnd ?? schedule.Start;
        if (overtimeStart < schedule.Start) overtimeStart = schedule.Start;
        var record = new OvertimeRecord(state.WorkDate, schedule.Start, schedule.NormalEnd, state.FinishedAt!.Value,
            CountdownCalculator.CalculateActualOvertime(overtimeStart, state.FinishedAt.Value).Ticks);
        record.Validate();
        return record;
    }

    private void SaveFinishedOvertime(CountdownState state, WorkSchedule schedule)
    {
        try { _pendingOvertime[state.WorkDate] = (MakeOvertimeRecord(state, schedule), false); }
        catch (Exception error) when (IsStorageError(error))
        {
            OvertimeHistoryError = "已下班，但本次时间无效，无法生成加班记录。";
            return;
        }
        RefreshOvertimeHistory();
    }

    private void RecoverFinishedOvertime(CountdownState state)
    {
        if (state.FinishedAt is not DateTime finished || finished > _now()) return;
        // A stale countdown state must never overwrite a later explicitly recorded finish.
        if (_allOvertimeRecords.Any(r => r.WorkDate == state.WorkDate && r.FinishedAt >= finished)) return;
        try { _pendingOvertime[state.WorkDate] = (MakeOvertimeRecord(state, Calculate(state)), true); }
        catch (Exception error) when (IsStorageError(error))
        {
            OvertimeHistoryError = "上次下班记录的时间无效，未自动补记；已有加班历史保持不变。";
            return;
        }
        RefreshOvertimeHistory();
    }

    private void ShowOvertimeMonth()
    {
        OvertimeRecords.Clear();
        var selected = _allOvertimeRecords.Where(r => r.WorkDate.Year == _selectedOvertimeMonth.Year
            && r.WorkDate.Month == _selectedOvertimeMonth.Month).OrderByDescending(r => r.WorkDate).ToArray();
        foreach (var record in selected) OvertimeRecords.Add(new(record));
        decimal ticks = selected.Sum(r => (decimal)r.OvertimeTicks);
        _overtimeSummary = $"已记录 {selected.Length} 天 · 实际加班合计 {FormatOvertime(ticks)}";
        foreach (string property in new[] { nameof(OvertimeMonthText), nameof(OvertimeSummaryText),
                     nameof(HasOvertimeRecords), nameof(HasNoOvertimeRecords) }) OnPropertyChanged(property);
        PreviousOvertimeMonthCommand.RaiseCanExecuteChanged();
        NextOvertimeMonthCommand.RaiseCanExecuteChanged();
    }

    internal static string FormatOvertime(decimal ticks)
    {
        long seconds = (long)decimal.Floor(ticks / TimeSpan.TicksPerSecond);
        string text = $"{seconds / 3600}小时{seconds / 60 % 60}分钟";
        return seconds % 60 == 0 ? text : $"{text}{seconds % 60}秒";
    }

    private static DateTime MonthOf(DateTime value) => new(value.Year, value.Month, 1);

    private void FollowCurrentOvertimeMonth()
    {
        if (_followCurrentOvertimeMonth && _selectedOvertimeMonth != MonthOf(_now())) RefreshOvertimeHistory();
    }

    private void StartOvertimeMonthTimer()
    {
        // Finishing stops the countdown timer; month rollover still needs a lightweight check.
        _overtimeMonthTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
        _overtimeMonthTimer.Tick += OnOvertimeMonthTick;
        _overtimeMonthTimer.Start();
    }

    private void OnOvertimeMonthTick(object? sender, EventArgs e) => FollowCurrentOvertimeMonth();

    private void DisposeOvertimeHistory()
    {
        if (_overtimeMonthTimer is not null)
        {
            _overtimeMonthTimer.Stop();
            _overtimeMonthTimer.Tick -= OnOvertimeMonthTick;
        }
        PreviousOvertimeMonthCommand.RaiseCanExecuteChanged();
        NextOvertimeMonthCommand.RaiseCanExecuteChanged();
        CurrentOvertimeMonthCommand.RaiseCanExecuteChanged();
        RefreshOvertimeHistoryCommand.RaiseCanExecuteChanged();
    }
}
