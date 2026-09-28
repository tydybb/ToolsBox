namespace ToolsBox.Core.WorkCountdown;

/// <summary>
/// 加班进行中的趣味文案：当有加班任务在跑、或已经进入无偿加班状态时兜底显示，
/// 避免"睡到自然醒""胜利在望"这类躺平系文案与同屏的计划加班、无偿加班提示互相打架。
/// 仅在当天没有节日文案时使用（节日文案此时已是"别人在旅游我在加班"的同方向吐槽）。
/// 按日期轮换，同一天内文案稳定；不依赖节假日日历，任意年份均可使用。
/// </summary>
public static class OvertimeFunCopy
{
    private static readonly string[] CountdownLines =
    [
        "加班进行中：头发和进度条，总要掉一个",
        "这一段加班叫硬扛，扛完记得吃饭",
        "工位长在屁股上了，这段加班先熬完",
        "别人在过日子，我在为 KPI 加班，命苦但先干着",
        "今天的命是咖啡给的，班是自己选的，含泪加完"
    ];

    /// <summary>加班进行时的倒计时文案；任意日期均非空。</summary>
    public static string Countdown(DateOnly date) => CountdownLines[date.DayNumber % CountdownLines.Length];
}
