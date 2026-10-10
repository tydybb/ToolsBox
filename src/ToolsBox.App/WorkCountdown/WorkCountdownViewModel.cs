using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using ToolsBox.App.Infrastructure;
using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.App.WorkCountdown;

public sealed record CountdownDayChoice(CountdownDayMode Mode, string Label);

public sealed class WorkCountdownViewModel : ObservableObject, IDisposable
{
    private readonly Func<DateTime> _now;
    private readonly ICountdownStateStore _store;
    private readonly DispatcherTimer? _timer;
    private DateTime _currentTime;
    private string _startTimeText = "08:30";
    private string _overtimeText = "";
    private DateOnly _overtimeInputDate;
    private CountdownDayMode _dayMode;
    private CountdownState? _activeState;
    private WorkSchedule? _schedule;
    private string _errorMessage = "";
    private bool _hasRemindedOffWork;
    private bool _disposed;
    private bool _overtimeEdited;
    private bool _inputsEdited;
    private CountdownReaction _reaction = null!;
    private CountdownCardPresentation _card = null!;
    private bool _animationEnabled = true;

    public WorkCountdownViewModel(Func<DateTime>? now = null, ICountdownStateStore? store = null, bool startTimer = true)
    {
        _now = now ?? (() => DateTime.Now);
        _store = store ?? new JsonCountdownStateStore();
        _currentTime = _now();
        _overtimeInputDate = Today;
        StartCommand = new RelayCommand(Start, () => !_disposed);
        ResetCommand = new RelayCommand(Reset, () => !_disposed);
        UseCurrentTimeCommand = new RelayCommand(UseCurrentTime, () => !_disposed);
        FinishCommand = new RelayCommand(Finish, CanFinish);
        Restore();
        _reaction = SelectReaction();
        _card = CountdownCardPresentationSelector.Select(ReactionContext());
        if (startTimer)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += OnTick;
            if (!IsFinished && string.IsNullOrEmpty(ErrorMessage)) _timer.Start();
        }
    }

    public IReadOnlyList<CountdownDayChoice> DayChoices { get; } =
    [
        new(CountdownDayMode.Automatic, "自动识别（2026 日历）"),
        new(CountdownDayMode.Workday, "手动：工作日 / 调休补班"),
        new(CountdownDayMode.RestDay, "手动：周末 / 节假日加班")
    ];

    public RelayCommand StartCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand UseCurrentTimeCommand { get; }
    public RelayCommand FinishCommand { get; }
    public event EventHandler? WorkFinished;
    public event EventHandler? OffWorkReached;
    private DateOnly Today => DateOnly.FromDateTime(_currentTime);

    public string StartTimeText
    {
        get => _startTimeText;
        set { if (SetProperty(ref _startTimeText, value)) InputsChanged(); }
    }

    public string OvertimeText
    {
        get => _overtimeText;
        set
        {
            _overtimeInputDate = DateOnly.FromDateTime(_now());
            if (SetProperty(ref _overtimeText, value)) { _overtimeEdited = true; InputsChanged(); }
        }
    }

    public CountdownDayMode DayMode
    {
        get => _dayMode;
        set
        {
            if (!SetProperty(ref _dayMode, value)) return;
            InputsChanged();
        }
    }

    private DayKind EffectiveKind => DayMode switch
    {
        CountdownDayMode.Automatic => ChinaWorkCalendar.GetDay(Today).Kind,
        CountdownDayMode.Workday => DayKind.Workday,
        CountdownDayMode.RestDay => DayKind.RestDay,
        _ => DayKind.Unknown
    };

    public WorkSchedule? Schedule => _schedule;
    public CountdownReaction Reaction => _reaction;
    public CountdownCardPresentation Card => _card;
    public bool AnimationEnabled { get => _animationEnabled; set => SetProperty(ref _animationEnabled, value); }
    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public string DateText => _currentTime.ToString("yyyy-MM-dd dddd", CultureInfo.GetCultureInfo("zh-CN"));
    public string CalendarText => DayMode switch
    {
        CountdownDayMode.Workday => "已手动指定：工作日 / 调休补班日",
        CountdownDayMode.RestDay => "已手动指定：周末 / 节假日（全天加班）",
        _ => ChinaWorkCalendar.GetDay(Today).Label
    };
    public string OvertimeHint => EffectiveKind == DayKind.RestDay
        ? "全天加班：留空按 0 处理；开始计时需整数小时，至少 4 小时"
        : "工作日：留空或 0 表示不加班；加班为整数，至少 2 小时";
    public bool HasPendingChanges => _activeState is not null && (
        _activeState.WorkDate != Today || _activeState.StartTime != StartTimeText.Trim()
        || _activeState.Mode != DayMode || _overtimeEdited &&
        (!TryParseOvertime(OvertimeText, out int hours) || _activeState.OvertimeHours != hours));
    public string PendingText => HasPendingChanges ? "输入已变更；仍显示上次任务，请点击“开始 / 重新计算”应用。" : "";
    public string ActiveTaskText => _activeState is null ? "尚未开始" :
        $"任务日期：{_activeState.WorkDate:yyyy-MM-dd}  ·  上班：{_activeState.StartTime}  ·  加班：{_activeState.OvertimeHours} 小时";
    public string AttendanceText => _schedule is null ? "—" :
        IsEarlyDeparture ? (_schedule.IsLate ? "今日迟到 · 早退" : "今日早退") : _schedule.IsLate ? "今日迟到" :
        _schedule.NormalEnd is null ? "全天加班" : "正常出勤";
    public string NormalEndText => _schedule is null ? "—" : _schedule.NormalEnd?.ToString("HH:mm") ?? "全天按加班计算";
    public string EndText => _schedule?.End.ToString("yyyy-MM-dd HH:mm") ?? "—";
    public string BreakText => _schedule is null ? "—" : $"{_schedule.ExcludedBreaks.TotalMinutes:0} 分钟（仅统计加班内休息）";
    public bool HasOvertime => _schedule is not null && _activeState is { OvertimeHours: > 0 };
    public DateTime? FinishedAt => _activeState?.FinishedAt;
    public bool IsFinished => FinishedAt.HasValue;
    private DateTime DisplayTime => FinishedAt ?? _currentTime;
    public bool IsEarlyDeparture => FinishedAt is DateTime finished && _schedule?.NormalEnd is DateTime normal && finished < normal;
    public bool HasIncompleteOvertime => IsFinished && HasOvertime && FinishedAt < _schedule!.End;
    public string FinishMessage => !IsFinished ? "" : IsEarlyDeparture
        ? $"你早退了，距离正常下班还差 {Math.Ceiling((_schedule!.NormalEnd!.Value - FinishedAt!.Value).TotalMinutes):0} 分钟。"
        : "已下班，抓紧回家吧！";
    public string FinishDetails => !IsFinished ? "" : $"实际下班：{FinishedAt:yyyy-MM-dd HH:mm:ss}"
        + (HasIncompleteOvertime ? " · 计划加班未完成" : "")
        + (HasIncompleteOvertime && !IsEarlyDeparture ? " · 不计为早退" : "");
    public string FinishedTimerLabel => !IsFinished ? "" : DisplayTime >= _schedule!.End
        ? "超出计划时间（计时已停止）" : "距预计下班（计时已停止）";
    public string OvertimeDurationText => HasOvertime ? $"计划加班：{_activeState!.OvertimeHours} 小时（不含休息）" : "";
    public string OvertimePeriodText => HasOvertime
        ? $"加班时段：{_schedule!.NormalEnd ?? _schedule.Start:yyyy-MM-dd HH:mm} — {_schedule.End:yyyy-MM-dd HH:mm}"
        : "";
    public string OvertimeMessage
    {
        get
        {
            if (!HasOvertime) return "";
            int hours = _activeState!.OvertimeHours;
            int unrealisticThreshold = _schedule!.NormalEnd is null ? 22 : 15;
            if (hours >= unrealisticThreshold)
                return $"别吹牛逼啦，{hours} 小时加班？你是来上班，还是来修仙的？先歇会儿！";
            return hours switch
            {
                <= 3 => "加班小副本：忙完这一段，就回家领取快乐。",
                <= 5 => "续航模式已开启，别忘了喝水、起身活动一下。",
                <= 8 => "今天是加长版，工作可以续集，休息可别跳过。",
                _ => "身体不是无限续航，这班有点长，能歇就歇，别硬扛。"
            };
        }
    }
    public string RemainingText
    {
        get
        {
            if (_schedule is null) return "--:--:--";
            long seconds = (long)Math.Ceiling(Math.Max(0, (_schedule.End - DisplayTime).TotalSeconds));
            return $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
        }
    }
    public bool IsOverdue => !IsFinished && _schedule is not null && _currentTime >= _schedule.End;
    public string ElapsedText
    {
        get
        {
            if (_schedule is null) return "--:--:--";
            long seconds = (long)Math.Floor(Math.Max(0, (DisplayTime - _schedule.End).TotalSeconds));
            return $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
        }
    }
    public string TimerText => _schedule is not null && DisplayTime >= _schedule.End ? ElapsedText : RemainingText;
    public string StatusText => IsFinished ? FinishMessage : _schedule is null ? "输入上班时间，开始今日倒计时" :
        IsOverdue ? "你已无偿加班" :
        _currentTime < _schedule.Start ? "尚未到上班时间，显示距离预计下班的时间" : "距离预计下班";

    /// <summary>
    /// 趣味文案：倒计时阶段用“倒计时语气”，到点下班（“即将关机”提醒弹出）或点击“下班”后切换为“收工语气”。
    /// 优先节日场景（假期中 / 节前 / 调休 / 节后 / 7 天内倒计时）；
    /// 无节日气氛且有加班任务在跑（或已无偿加班）时用加班语气，避免躺平文案与加班提示同屏打架；
    /// 两者皆无时兜底为周一至周日的星期文案。
    /// 收工语气的“还有 N 天”从明天起算（今天已过完），所以同一日期白天是 N 天、下班时是 N-1 天。
    /// </summary>
    public string FlavorText
    {
        get
        {
            DateOnly date = DateOnly.FromDateTime(DisplayTime);
            DayKind kind = GetAppliedKind(date);
            HolidayMoment moment = HolidayFunCopy.Detect(date);
            bool offWorkMoment = IsFinished || IsOverdue;
            bool workerOnlyMoment = moment.Kind is HolidayMomentKind.MakeupWorkday
                or HolidayMomentKind.HolidayEve or HolidayMomentKind.AfterHoliday;
            string holiday = kind == DayKind.RestDay && workerOnlyMoment ? "" : offWorkMoment
                ? HolidayFunCopy.OffWork(moment, date) : HolidayFunCopy.Countdown(moment, date);
            if (holiday.Length > 0) return holiday;
            if (IsOvertimeFlavorActive) return OvertimeFunCopy.Countdown(date);
            return offWorkMoment ? WeekdayFunCopy.OffWork(date, kind) : WeekdayFunCopy.Countdown(date, kind);
        }
    }

    /// <summary>倒计时未结束且有加班任务在跑、或已进入无偿加班状态。</summary>
    private bool IsOvertimeFlavorActive => !IsFinished && _schedule is not null && (HasOvertime || IsOverdue);

    public bool HasFlavor => FlavorText.Length > 0;

    /// <summary>
    /// 下班庆祝特效分级：节前最后一个工作日或法定假期内才有，提醒窗据此撒纸屑 / 放烟花。
    /// 与趣味文案同源，按下班时刻的日期判定，非假期日子为 None。
    /// </summary>
    public CelebrationLevel CelebrationLevel => HolidayCelebration.Detect(DateOnly.FromDateTime(DisplayTime));

    public void Refresh()
    {
        if (_disposed) return;
        _currentTime = _now();
        ClearExpiredOvertimeInput(_currentTime);
        NotifyDisplay();
        RemindIfOffWorkReached();
    }

    /// <summary>Only a real same-day clock-in can start a timer. Manual acknowledgement never calls this.</summary>
    public bool ImportClockIn(DateTime clockIn, Func<bool> confirmReplacement, CountdownDayMode? inferredMode = null)
    {
        DateTime now = _now();
        if (_disposed || clockIn.Date != now.Date || clockIn > now || clockIn.TimeOfDay < new TimeSpan(7,30,0)) return false;
        ClearExpiredOvertimeInput(now, preserveAppliedPlan: false);
        if (IsFinished && _activeState?.WorkDate == DateOnly.FromDateTime(now)) return false;
        string time = clockIn.ToString("HH:mm",CultureInfo.InvariantCulture);
        CountdownDayMode mode=inferredMode??DayMode;
        string overtime=inferredMode==CountdownDayMode.Workday && _activeState is null && !_overtimeEdited?"0":OvertimeText;
        try
        {
            if (!TryParseOvertime(overtime, out int hours)) throw new ArgumentException("加班时间必须是非负整数小时。");
            _=Calculate(new(DateOnly.FromDateTime(now),time,mode,hours));
        }
        catch(ArgumentException e){ErrorMessage=e.Message;return false;}
        bool same=_activeState?.WorkDate == DateOnly.FromDateTime(now) && _activeState.StartTime == time && _activeState.Mode==mode;
        if(same && !_inputsEdited && string.IsNullOrEmpty(ErrorMessage))return true;
        bool conflict = _activeState?.WorkDate == DateOnly.FromDateTime(now) ||
            _inputsEdited;
        var expectedState=_activeState;string expectedInput=StartTimeText,expectedOvertime=OvertimeText;var expectedMode=DayMode;
        if (conflict && (!same || _inputsEdited) && !confirmReplacement()) return false;
        // Confirmation can run a nested message loop; never overwrite a newly finished task.
        if (_disposed || _now().Date != now.Date || IsFinished && _activeState?.WorkDate == DateOnly.FromDateTime(now) ||
            _activeState!=expectedState || StartTimeText!=expectedInput || OvertimeText!=expectedOvertime || DayMode!=expectedMode) return false;
        string old = StartTimeText;
        DayMode=mode;OvertimeText=overtime;
        StartTimeText = time;
        Start();
        if (_schedule?.Start.Date == now.Date && _schedule.Start.ToString("HH:mm",CultureInfo.InvariantCulture) == time) return string.IsNullOrEmpty(ErrorMessage);
        StartTimeText = old;
        return false;
    }

    private void UseCurrentTime()
    {
        if (_disposed) return;
        Refresh();
        StartTimeText = _currentTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        ErrorMessage = _currentTime.TimeOfDay < TimeSpan.FromHours(7.5) ? "上班时间不能早于 07:30。" : "";
    }

    private bool CanFinish() => !_disposed && !IsFinished && _schedule is not null && _now() >= _schedule.Start;

    private void Finish()
    {
        if (_disposed || IsFinished || _schedule is null || _activeState is null) return;
        DateTime finished = _now();
        if (finished < _schedule.Start) { FinishCommand.RaiseCanExecuteChanged(); return; }
        _currentTime = finished;
        _activeState = _activeState with { FinishedAt = finished };
        ClearOvertimeInput(finished);
        _hasRemindedOffWork = true;
        _timer?.Stop();
        ErrorMessage = "";
        try { _store.Save(_activeState); }
        catch (Exception error) when (IsStorageError(error))
        { ErrorMessage = "已下班并停止计时，但无法保存记录，重启后可能恢复计时。"; }
        NotifyDisplay();
        WorkFinished?.Invoke(this, EventArgs.Empty);
    }

    private void Start()
    {
        if (_disposed) return;
        // Apply a replacement task before checking its deadline, so the old task cannot alert here.
        _currentTime = _now();
        ClearExpiredOvertimeInput(_currentTime, preserveAppliedPlan: false);
        NotifyDisplay();
        try
        {
            if (!TryParseOvertime(OvertimeText, out int hours))
                throw new ArgumentException("加班时间必须是非负整数小时。");
            var state = new CountdownState(Today, StartTimeText.Trim(), DayMode, hours);
            WorkSchedule schedule = Calculate(state);
            state = state with { StartTime = schedule.Start.ToString("HH:mm", CultureInfo.InvariantCulture) };
            if (_activeState != state) _hasRemindedOffWork = false;
            _activeState = state;
            _schedule = schedule;
            _timer?.Start();
            StartTimeText = state.StartTime;
            ErrorMessage = "";
            try { _store.Save(state); _inputsEdited = false; }
            catch (Exception error) when (IsStorageError(error))
            { ErrorMessage = "倒计时已开始，但无法保存记录，关闭后可能无法恢复。"; }
            NotifyDisplay();
        }
        catch (ArgumentException error) { ErrorMessage = error.Message; return; }
        RemindIfOffWorkReached();
    }

    private void RemindIfOffWorkReached()
    {
        if (_hasRemindedOffWork || !IsOverdue) return;
        _hasRemindedOffWork = true;
        OffWorkReached?.Invoke(this, EventArgs.Empty);
    }

    private static WorkSchedule Calculate(CountdownState state)
    {
        if (!TimeOnly.TryParseExact(state.StartTime?.Replace('：', ':'), ["H:mm", "HH:mm", "HHmm"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out TimeOnly start))
            throw new ArgumentException("请输入有效的上班时间，例如 08:30、08：30 或四位数字 0830。");
        DayKind kind = state.Mode switch
        {
            CountdownDayMode.Automatic => ChinaWorkCalendar.GetDay(state.WorkDate).Kind,
            CountdownDayMode.Workday => DayKind.Workday,
            CountdownDayMode.RestDay => DayKind.RestDay,
            _ => throw new ArgumentException("日期类型无效，请重新选择。")
        };
        if (kind == DayKind.Unknown)
            throw new ArgumentException("缺少该年度的节假日日历，请手动选择工作日或周末 / 节假日。内置日历仅支持 2026 年。");
        return CountdownCalculator.Calculate(state.WorkDate, start, kind, state.OvertimeHours);
    }

    private void Restore()
    {
        try
        {
            CountdownState? saved = _store.Load();
            if (saved is null || saved.WorkDate != Today) return;
            WorkSchedule schedule = Calculate(saved);
            if (saved.FinishedAt is DateTime finished && finished < schedule.Start)
                throw new InvalidDataException("下班时间早于上班时间。");
            _activeState = saved;
            _schedule = schedule;
            _startTimeText = saved.StartTime;
            _dayMode = saved.Mode;
            _overtimeText = saved.FinishedAt.HasValue ? "" : saved.OvertimeHours.ToString(CultureInfo.InvariantCulture);
            _hasRemindedOffWork = saved.FinishedAt.HasValue;
        }
        catch (Exception error) when (IsStorageError(error))
        { ErrorMessage = "无法恢复上次倒计时记录，请重新输入并开始。"; }
    }

    private void Reset()
    {
        if (_disposed) return;
        _activeState = null;
        _schedule = null;
        _hasRemindedOffWork = false;
        _timer?.Start();
        ErrorMessage = "";
        try { _store.Clear(); }
        catch (Exception error) when (IsStorageError(error))
        { ErrorMessage = "倒计时已重置，但保存记录未能删除，重启后可能恢复旧任务。"; }
        Refresh();
    }

    private static bool IsStorageError(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException
        or JsonException or ArgumentException or NotSupportedException or System.Security.SecurityException;

    private static bool TryParseOvertime(string? text, out int hours)
    {
        hours = 0;
        string? input = text?.Trim();
        return string.IsNullOrEmpty(input)
            || int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out hours);
    }

    private void ClearExpiredOvertimeInput(DateTime now, bool preserveAppliedPlan = true)
    {
        if (DateOnly.FromDateTime(now) <= _overtimeInputDate) return;
        // Reaching an overnight deadline still starts unpaid timing; only Finish clears it that day.
        if (preserveAppliedPlan && !IsFinished && _schedule is not null
            && DateOnly.FromDateTime(now) <= DateOnly.FromDateTime(_schedule.End)) return;
        ClearOvertimeInput(now);
    }

    private void ClearOvertimeInput(DateTime now)
    {
        _overtimeInputDate = DateOnly.FromDateTime(now);
        _overtimeEdited = false;
        SetProperty(ref _overtimeText, "", nameof(OvertimeText));
    }

    private void InputsChanged()
    {
        _inputsEdited = true;
        ErrorMessage = "";
        NotifyDisplay();
    }

    private void NotifyDisplay()
    {
        foreach (string property in new[] { nameof(Schedule), nameof(DateText), nameof(CalendarText), nameof(OvertimeHint),
            nameof(HasPendingChanges), nameof(PendingText), nameof(ActiveTaskText), nameof(AttendanceText), nameof(NormalEndText),
            nameof(EndText), nameof(BreakText), nameof(HasOvertime), nameof(OvertimeDurationText), nameof(OvertimePeriodText),
            nameof(OvertimeMessage), nameof(RemainingText), nameof(IsOverdue), nameof(ElapsedText), nameof(TimerText),
            nameof(StatusText), nameof(IsFinished), nameof(FinishedAt), nameof(IsEarlyDeparture),
            nameof(HasIncompleteOvertime), nameof(FinishMessage), nameof(FinishDetails), nameof(FinishedTimerLabel),
            nameof(FlavorText), nameof(HasFlavor), nameof(CelebrationLevel) }) OnPropertyChanged(property);
        FinishCommand.RaiseCanExecuteChanged();
        SetProperty(ref _reaction, SelectReaction(), nameof(Reaction));
        SetProperty(ref _card, CountdownCardPresentationSelector.Select(ReactionContext()), nameof(Card));
    }

    private CountdownReaction SelectReaction() => CountdownReactionSelector.Select(ReactionContext());

    private CountdownReactionContext ReactionContext()
    {
        DateOnly date = _activeState?.WorkDate ?? Today;
        DayKind kind = GetAppliedKind(date);
        return new(date, kind, _schedule, _currentTime,
            _activeState?.FinishedAt, _activeState?.OvertimeHours ?? 0);
    }

    private DayKind GetAppliedKind(DateOnly date) => _activeState?.WorkDate == date
        ? _activeState.Mode switch
        {
            CountdownDayMode.Workday => DayKind.Workday,
            CountdownDayMode.RestDay => DayKind.RestDay,
            _ => ChinaWorkCalendar.GetDay(date).Kind
        }
        : ChinaWorkCalendar.GetDay(date).Kind;

    private void OnTick(object? sender, EventArgs e) => Refresh();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_timer is not null) { _timer.Stop(); _timer.Tick -= OnTick; }
        StartCommand.RaiseCanExecuteChanged();
        ResetCommand.RaiseCanExecuteChanged();
        UseCurrentTimeCommand.RaiseCanExecuteChanged();
        FinishCommand.RaiseCanExecuteChanged();
    }
}
