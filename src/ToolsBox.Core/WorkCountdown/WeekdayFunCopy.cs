namespace ToolsBox.Core.WorkCountdown;

/// <summary>
/// 结合当天有效类型与次日日历的日常趣味文案。
/// 多条文案按日期轮换，同一天内稳定；无年度日历时只描述当天，不承诺休假。
/// </summary>
public static class WeekdayFunCopy
{
    // 数组下标按 (int)DayOfWeek：0=周日 … 6=周六。
    private static readonly string[][] CountdownLines =
    [
        // 周日
        [
            "给今天的自己续点电，慢慢把任务收好",
            "先忙好眼前这一段，记得喝口水",
            "日历继续翻，今天也按自己的节奏来"
        ],
        // 周一
        [
            "周一综合征：人到了，灵魂还在被窝里请假",
            "还没开始好，先续杯咖啡",
            "先把今天开个好头，给生活留点电"
        ],
        // 周二
        [
            "日历说还早，身体说好累",
            "咖啡先到位，任务一件件来",
            "给坚持到现在的自己点个赞"
        ],
        // 周三
        [
            "忙里偷个小休息，给自己续点电",
            "任务一件件收好，脚步可以慢一点",
            "给坚持到现在的自己点个赞"
        ],
        // 周四
        [
            "先稳住今天的节奏，忙完再歇一歇",
            "给今天的自己续杯咖啡",
            "今天也值得一个深呼吸，任务慢慢来"
        ],
        // 周五
        [
            "给今天的自己加点快乐",
            "把今天忙完，再切到生活频道",
            "快乐也可以是今天的 KPI"
        ],
        // 周六
        [
            "咖啡先来陪你上场，任务一件件来",
            "忙今天的任务，也要照顾好自己",
            "稳住今天的节奏，忙完记得歇一歇"
        ]
    ];

    private static readonly string[][] OffWorkLines =
    [
        // 周日
        [
            "收工，把今天的尾巴好好收掉",
            "下班，把今晚留给生活"
        ],
        // 周一
        [
            "收工，今晚好好充电",
            "今天这一关过完了，给自己一个击掌"
        ],
        // 周二
        [
            "收工，今天的任务先告一段落",
            "下班，给今天的努力加个鸡腿"
        ],
        // 周三
        [
            "下班，今晚值得一个小奖励",
            "收工，给坚持到现在的自己加个鸡腿"
        ],
        // 周四
        [
            "收工，今晚先把电充回来",
            "下班，把工作收好，让今晚回到生活里"
        ],
        // 周五
        [
            "收工，给今天的自己一个击掌",
            "下班快乐，今晚把时间留给自己"
        ],
        // 周六
        [
            "收工，忙完这一段也要照顾好自己",
            "下班，把今晚留给生活"
        ]
    ];

    private static readonly string[] RestCountdownLines =
    [
        "今天按自己的节奏来，给生活留点小快乐",
        "把空闲留给想做的事，也给自己续点电",
        "时间慢一点，小快乐也能多一点"
    ];

    private static readonly string[] UnknownCountdownLines =
    [
        "今天一步一步来，给自己续点电",
        "按今天的节奏来，记得歇一歇",
        "给今天的自己留点小快乐"
    ];

    /// <summary>倒计时阶段的星期文案；手动类型只应用于当天。</summary>
    public static string Countdown(DateOnly date) => Countdown(date, null);

    public static string Countdown(DateOnly date, DayKind? appliedKind)
    {
        DayKind kind = appliedKind ?? ChinaWorkCalendar.GetDay(date).Kind;
        string[] lines = kind switch
        {
            DayKind.RestDay => RestCountdownLines,
            DayKind.Workday => CountdownLines[(int)date.DayOfWeek],
            _ => UnknownCountdownLines
        };
        return $"{DayDescription(date, kind, false)}，{Pick(lines, date.DayNumber)}";
    }

    /// <summary>下班时刻的星期文案；任意日期均非空。</summary>
    public static string OffWork(DateOnly date) => OffWork(date, null);

    public static string OffWork(DateOnly date, DayKind? appliedKind)
    {
        DayKind kind = appliedKind ?? ChinaWorkCalendar.GetDay(date).Kind;
        return $"{DayDescription(date, kind, true)}，{Pick(OffWorkLines[(int)date.DayOfWeek], date.DayNumber)}";
    }

    /// <summary>当天有效类型与已知次日安排；不把当天的手动类型延伸到明天。</summary>
    public static string DayDescription(DateOnly date, DayKind kind, bool hasTask, DateOnly? referenceDate = null)
    {
        string weekday = date.DayOfWeek switch
        {
            DayOfWeek.Monday => "周一",
            DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五",
            DayOfWeek.Saturday => "周六",
            _ => "周日"
        };
        HolidaySpan? holiday = ChinaWorkCalendar.GetHolidayAt(date);
        string today = kind switch
        {
            DayKind.RestDay => holiday is HolidaySpan restHoliday
                ? $"{restHoliday.Name}假期{(hasTask ? "加班" : "")}"
                : hasTask ? "休息日加班" : "今天休息",
            DayKind.Workday when holiday is HolidaySpan workHoliday => $"{workHoliday.Name}假期上班",
            DayKind.Workday when ChinaWorkCalendar.IsMakeupWorkday(date) => "调休补班",
            DayKind.Workday when date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday => "周末上班",
            DayKind.Workday => "",
            _ => "日期类型待确认"
        };
        DateOnly reference = referenceDate ?? date;
        string day = reference == date ? weekday : $"{date:yyyy-MM-dd} {weekday}任务";
        string description = today.Length == 0 ? day : $"{day}，{today}";
        string tomorrow = TomorrowDescription(reference);
        return tomorrow.Length == 0 ? description : $"{description}，{tomorrow}";
    }

    /// <summary>次日法定日历安排；无日历数据或已到日期上界时返回空串。</summary>
    public static string TomorrowDescription(DateOnly date)
    {
        if (date == DateOnly.MaxValue) return "";
        DateOnly tomorrow = date.AddDays(1);
        return ChinaWorkCalendar.GetDay(tomorrow).Kind switch
        {
            DayKind.Workday when ChinaWorkCalendar.IsMakeupWorkday(tomorrow) => "明天调休补班",
            DayKind.Workday => "明天上班",
            DayKind.RestDay => ChinaWorkCalendar.GetHolidayAt(tomorrow) is HolidaySpan holiday
                ? $"明天休息（{holiday.Name}假期）" : "明天休息",
            _ => ""
        };
    }

    private static string Pick(string[] lines, int dayNumber) => lines[dayNumber % lines.Length];
}
