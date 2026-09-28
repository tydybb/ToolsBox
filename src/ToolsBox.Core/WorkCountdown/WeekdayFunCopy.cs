namespace ToolsBox.Core.WorkCountdown;

/// <summary>
/// 周一至周日的日常趣味文案，取材自网络上对一周七天的普遍情绪
/// （周一综合征、周二难熬、周三小周末、周四盼周五、周五快乐、周六躺平、周日收心）。
/// 每天提供多条按日期轮换，同一天内文案稳定；仅在当天没有节日文案时兜底显示，
/// 不依赖节假日日历，任意年份均可使用。
/// </summary>
public static class WeekdayFunCopy
{
    // 数组下标按 (int)DayOfWeek：0=周日 … 6=周六。
    private static readonly string[][] CountdownLines =
    [
        // 周日
        [
            "周日：假期余额不足，且用且珍惜",
            "周日晚上的风，已经开始捎上周一的味道",
            "周日宜收心、宜早睡，明日再战"
        ],
        // 周一
        [
            "周一综合征：人到了，灵魂还在被窝里请假",
            "周一不好，只是还没开始好，先续杯咖啡",
            "周一：把周末存进银行，开始七天的还贷"
        ],
        // 周二
        [
            "周二是一周里最漫长的一天，周一的后悔还没消化完",
            "周二，日历说还早，身体说好累",
            "熬过周二，这周就算过了一半——虽然才过了两天"
        ],
        // 周三
        [
            "周三小周末，一周过半，胜利在望",
            "周三是分水岭，往后都是下坡路——往周末的方向",
            "周三了，给坚持了三天的自己点个赞"
        ],
        // 周四
        [
            "周四：周五的前奏，风里已经有周末的味道",
            "周四过完，这周基本就算拿下了",
            "周四，倒数第二个工作日的自我修养"
        ],
        // 周五
        [
            "周五快乐！今天的风都是甜的",
            "周五：下班铃一响，世界都是你的",
            "周五，快乐就是今天的 KPI"
        ],
        // 周六
        [
            "周六的正确姿势：睡到自然醒",
            "周六：自由的味道，从赖床开始",
            "周六不谈工作，谈谈风月"
        ]
    ];

    private static readonly string[][] OffWorkLines =
    [
        // 周日
        [
            "周日收工，早睡早起迎周一",
            "周日下班，把这一周的尾巴也好好收掉"
        ],
        // 周一
        [
            "周一首战告捷，今晚好好充电",
            "周一准点收工，最难的一天过去了"
        ],
        // 周二
        [
            "周二收工，最难熬的一天过完了",
            "周二下班，离周末又近了两步"
        ],
        // 周三
        [
            "周三下班，一周过半，今晚值得庆祝一下",
            "周三收工，给坚持到现在的自己加个鸡腿"
        ],
        // 周四
        [
            "周四收工，明晚就是周五夜",
            "周四下班，周末已经在门口探头了"
        ],
        // 周五
        [
            "周五收工，周末模式即刻启动",
            "周五下班快乐，接下来的 48 小时归你"
        ],
        // 周六
        [
            "周六收工，把觉补回来",
            "周六下班，自由时间继续"
        ]
    ];

    /// <summary>倒计时阶段的星期文案；任意日期均非空。</summary>
    public static string Countdown(DateOnly date) => Pick(CountdownLines[(int)date.DayOfWeek], date.DayNumber);

    /// <summary>下班时刻的星期文案；任意日期均非空。</summary>
    public static string OffWork(DateOnly date) => Pick(OffWorkLines[(int)date.DayOfWeek], date.DayNumber);

    private static string Pick(string[] lines, int dayNumber) => lines[dayNumber % lines.Length];
}
