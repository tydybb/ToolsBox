namespace ToolsBox.Core.WorkCountdown;

public sealed record CountdownCardPresentation(
    string FactText,
    string Headline,
    string Caption,
    string SpeechText,
    string StageLabel,
    string EffectKey);

/// <summary>事实与玩笑分别生成；同一任务日期、角色和阶段内保持稳定。</summary>
public static class CountdownCardPresentationSelector
{
    public static CountdownCardPresentation Select(CountdownReactionContext context)
    {
        CountdownReaction reaction = CountdownReactionSelector.Select(context);
        Stage stage = GetStage(context);
        int seed = context.WorkDate.DayNumber + CharacterSeed(reaction.CharacterName) + (int)stage * 7;
        Copy copy = stage is Stage.Rest or Stage.Fresh or Stage.Working2 or Stage.Working5
            ? DayCopy(context, seed)
            : Pick(StageCopies[(int)stage], seed);
        DateOnly reference = DateOnly.FromDateTime(context.FinishedAt ?? context.Now);
        string fact = WeekdayFunCopy.DayDescription(context.WorkDate, context.Kind, context.Schedule is not null, reference);
        string holidayDetail = HolidayDetail(context, reference);
        if (holidayDetail.Length > 0) fact += $" · {holidayDetail}";

        string effect = Effect(stage);
        return new(fact, copy.Headline, copy.Caption,
            CountdownCharacterDialogue.Stage(reaction.CharacterName, effect, seed), StageLabel(stage), effect);
    }

    private static Stage GetStage(CountdownReactionContext context)
    {
        WorkSchedule? schedule = context.Schedule;
        if (context.FinishedAt is DateTime finished)
            return schedule?.NormalEnd is DateTime normalEnd && finished < normalEnd ? Stage.Early : Stage.Finished;
        if (schedule is not null && context.Now >= schedule.End) return Stage.Unpaid;
        if (schedule is not null && context.OvertimeHours >= (context.Kind == DayKind.RestDay ? 22 : 15))
            return Stage.Unreasonable;
        if (schedule is null) return context.Kind == DayKind.RestDay ? Stage.Rest : Stage.Ready;
        if (context.Now < schedule.Start) return Stage.Prestart;
        DateTime overtimeStart = schedule.NormalEnd ?? schedule.Start;
        if (context.OvertimeHours > 0 && context.Now >= overtimeStart)
        {
            double hours = (context.Now - overtimeStart).TotalHours;
            return hours >= 5 ? Stage.Overtime5 : hours >= 2 ? Stage.Overtime2 : Stage.Overtime;
        }
        if (schedule.End - context.Now <= TimeSpan.FromMinutes(30)) return Stage.Last30;
        double elapsed = (context.Now - schedule.Start).TotalHours;
        return elapsed >= 5 ? Stage.Working5 : elapsed >= 2 ? Stage.Working2 : Stage.Fresh;
    }

    private static Copy DayCopy(CountdownReactionContext context, int seed)
    {
        DateOnly date = context.WorkDate;
        if (context.Kind == DayKind.Unknown) return Pick(UnknownCopies, seed);
        if (ChinaWorkCalendar.GetHolidayAt(date) is HolidaySpan holiday)
        {
            Copy copy = Pick(context.Schedule is null && context.Kind == DayKind.RestDay ? HolidayRestCopies : HolidayWorkCopies, seed);
            return WithHolidayName(copy, holiday.Name);
        }
        // 当天的手动休息优先，不能继承补班或周末上班的口吻。
        if (context.Kind == DayKind.RestDay)
            return Pick(context.Schedule is null ? RestCopies : RestWorkCopies, seed);
        if (ChinaWorkCalendar.IsMakeupWorkday(date)) return Pick(MakeupCopies, seed);
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return Pick(WeekendWorkCopies, seed);

        DateOnly reference = DateOnly.FromDateTime(context.FinishedAt ?? context.Now);
        if (date.DayOfWeek == DayOfWeek.Friday && reference == date && reference < DateOnly.MaxValue
            && ChinaWorkCalendar.GetDay(reference.AddDays(1)).Kind == DayKind.Workday)
        {
            return Pick(FridayNextWorkCopies, seed);
        }

        Copy weekday = Pick(WeekdayCopies[(int)date.DayOfWeek], seed);
        HolidayMoment moment = SafeHolidayMoment(date);
        // 节前/节后选择完整的一组文案；具体假期天数只保留在事实区。
        if (moment.Kind is HolidayMomentKind.HolidayEve or HolidayMomentKind.AfterHoliday or HolidayMomentKind.NearHoliday)
        {
            return WithHolidayName(Pick(moment.Kind switch
            {
                HolidayMomentKind.HolidayEve => HolidayEveCopies,
                HolidayMomentKind.AfterHoliday => AfterHolidayCopies,
                _ => NearHolidayCopies
            }, seed), moment.Name);
        }
        return weekday;
    }

    private static string HolidayDetail(CountdownReactionContext context, DateOnly reference)
    {
        // 跨午夜仍显示任务日期，但相对假期天数不能继续套用旧日期。
        if (reference != context.WorkDate || context.Kind == DayKind.Unknown) return "";
        HolidayMoment moment = SafeHolidayMoment(context.WorkDate);
        if (moment.Kind == HolidayMomentKind.DuringHoliday)
            return moment.DaysLeft == 0 ? $"{moment.Name}假期最后一天"
                : $"{moment.Name}假期第 {moment.Days} 天，假期余额还剩 {moment.DaysLeft} 天";
        if (context.Kind == DayKind.RestDay) return "";
        return moment.Kind switch
        {
            HolidayMomentKind.AfterHoliday => moment.NextName.Length == 0
                ? $"{moment.Name}假期余额已清零"
                : $"{moment.Name}假期余额已清零，距离{moment.NextName}假期还有 {moment.Days} 天",
            HolidayMomentKind.NearHoliday => $"距离{moment.Name}假期还有 {moment.Days} 天",
            HolidayMomentKind.HolidayEve => $"{moment.Name}假期即将开始",
            HolidayMomentKind.MakeupWorkday when moment.Name.Length > 0 => $"距离{moment.Name}假期还有 {moment.Days} 天",
            _ => ""
        };
    }

    private static HolidayMoment SafeHolidayMoment(DateOnly date) =>
        date.Year == 2026 ? HolidayFunCopy.Detect(date) : default;

    private static Copy WithHolidayName(Copy copy, string name) => copy with
    {
        Headline = copy.Headline.Replace("{节日}", name, StringComparison.Ordinal),
        Caption = copy.Caption.Replace("{节日}", name, StringComparison.Ordinal)
    };

    private static Copy Pick(Copy[] copies, int seed) => copies[seed % copies.Length];

    private static int CharacterSeed(string character) => character switch
    {
        "厘普" => 0,
        "多栋" => 1,
        "秀珍" => 2,
        "田中" => 3,
        _ => 4
    };

    private static string Effect(Stage stage) => stage switch
    {
        Stage.Rest => "rest",
        Stage.Ready or Stage.Prestart => "idle",
        Stage.Last30 => "rush",
        Stage.Overtime or Stage.Overtime2 or Stage.Overtime5 => "overtime",
        Stage.Unpaid or Stage.Unreasonable => "overdue",
        Stage.Early or Stage.Finished => "celebrate",
        _ => "focus"
    };

    private static string StageLabel(Stage stage) => stage switch
    {
        Stage.Rest => "今日休息",
        Stage.Ready => "尚未开工",
        Stage.Prestart => "等待开工",
        Stage.Fresh => "刚刚开工",
        Stage.Working2 => "工作进行中",
        Stage.Working5 => "注意续电",
        Stage.Last30 => "最后半小时",
        Stage.Overtime => "计划加班中",
        Stage.Overtime2 => "加班进行中",
        Stage.Overtime5 => "加班较久",
        Stage.Unpaid => "计划已结束",
        Stage.Unreasonable => "计划过长",
        Stage.Finished => "已经下班",
        _ => "提前收工"
    };

    private enum Stage { Rest, Ready, Prestart, Fresh, Working2, Working5, Last30, Overtime, Overtime2, Overtime5, Unpaid, Unreasonable, Finished, Early }
    private sealed record Copy(string Headline, string Caption);

    private static readonly Copy[][] StageCopies =
    [
        [
            new("工位今日空置", "椅子今天独立办公。"),
            new("今日无事发生", "有事也等上班再发生。"),
            new("今天轮到床值班", "本人只负责躺着验收。")
        ],
        [
            new("还没开工，先算下班", "计划填一下，盼头不能没有。"),
            new("倒计时待上岗", "时间都没定，它也不好催。"),
            new("今天怎么个上法", "先排时间，再考虑表情。")
        ],
        [
            new("开工还没到点", "人可以早到，活不用。"),
            new("先坐着，别抢跑", "时间没到，积极容易白送。"),
            new("等待开工中", "咖啡先上班，我随后。")
        ],
        [
            new("刚开工，先别加戏", "今天的活已经够演了。"),
            new("开工了，表也开始走", "它走得比我积极。"),
            new("人已到位", "状态稍后补发。")
        ],
        [
            new("工作进行中", "表走得挺慢，活倒长得快。"),
            new("咖啡开始接班", "脑子休息一下，它顶着。"),
            new("工位坐出包浆了", "站一下，别让椅子认主。")
        ],
        [
            new("眼皮先下班了", "人还在，脑子改成远程了。"),
            new("坐得够久了", "再坐下去，椅子要收房租。"),
            new("咖啡也没话说", "工作继续，表情从简。")
        ],
        [
            new("下班已经到门口了", "椅子，你先适应一下没有我。"),
            new("最后半小时", "新活先别长出来，谢谢。"),
            new("开始收尾", "包已经比我进入状态了。")
        ],
        [
            new("计划加班，开始兑现", "是自己填的，也得允许自己后悔。"),
            new("加班按计划来了", "下班暂缓，叹气照常。"),
            new("加班时间到了", "这段有计划，脸色没预算。")
        ],
        [
            new("计划加班还在继续", "咖啡都凉了，活还挺热乎。"),
            new("加班两小时起步", "回家的路线已经背熟了。"),
            new("加班还没到尾声", "肚子先发言了。")
        ],
        [
            new("计划加班有点长", "五小时了，椅子快把我当配件了。"),
            new("加班加到咖啡沉默", "先停一下，脑子已经转空圈了。"),
            new("这段加班挺能熬", "活没熬熟，人快熟了。")
        ],
        [
            new("无偿加班，表先走了", "无偿加班：下班时间都走了，怎么就你不走？"),
            new("计划已结束", "无偿加班还在计时，工位倒挺会留客。"),
            new("下班时间已过", "无偿加班这段，谁给签个退场？")
        ],
        [
            new("计划过长，房租先算", "先别算加班了，算算房租。"),
            new("计划过长，不是续租", "这是排班，还是准备住下？"),
            new("计划过长，椅子都慌了", "时间填得下，人未必扛得住。")
        ],
        [
            new("下班了，话到此为止", "再说就算明天的了。"),
            new("今日已下班", "椅子归椅子，我归我。"),
            new("下班确认收到", "走了，工位自行运转。")
        ],
        [
            new("提前收工，先走为敬", "今天比闹钟有出息。"),
            new("提前下班已确认", "多出来的时间，先别上交。"),
            new("提前退场成功", "椅子还没反应过来。")
        ]
    ];

    // 下标保持 DayOfWeek 顺序；周末手动上班由 WeekendWorkCopies 接管。
    private static readonly Copy[][] WeekdayCopies =
    [
        [new("周日工位还开门", "周日也来上班，椅子挺感动。"), new("周日，表情从简", "工位有我，家里少我。"), new("周日照常办公", "这周的结尾有点费人。")],
        [new("周一，星期先自己上", "星期这么大了，应该学会自己上班。"), new("周一开机，脑子待定", "人到齐了，魂还在路上。"), new("周一又来了", "它怎么从来不用请假。")],
        [new("周二，不上不下", "离周一很近，离下班还挺远。"), new("周二正常发挥", "没什么大事，就是还得上班。"), new("周二夹在中间", "存在感不强，班一小时不少。")],
        [new("周三，过半了", "乐观一点，剩下的也得上。"), new("周三开始左右为难", "想走是真的，活还在也是真的。"), new("周三，卡在半路", "进度过半，表情没跟上。")],
        [new("KFC疯狂星期四，我没疯", "鸡块下锅了，我还在工位腌着。V我50分钟。"), new("KFC疯狂星期四，下班加急", "V我50分钟，给下班办个加急。"), new("KFC疯狂星期四，班挺脆", "V我50。鸡有脆皮，我有工牌。")],
        [new("周五，先看表再笑", "心已经走了，人还得签退。"), new("周五，有点坐不住", "椅子别挽留，大家都体面点。"), new("周五，下班正在排队", "今天的活最好有点眼力见。")],
        [new("周六还在工位", "周六上班，日历看了都沉默。"), new("周六照常营业", "别人出门，我也出门。目的地差点意思。"), new("周六这班挺醒目", "周末没消失，只是我没参与。")]
    ];

    private static readonly Copy[] RestCopies =
    [new("今天休息，工位随意", "椅子今天独立办公。"), new("今日不用打卡", "醒了也不代表要起。"), new("今天让床值班", "本人只负责躺着验收。")];
    private static readonly Copy[] FridayNextWorkCopies =
    [new("周五展示版", "周五，仅供展示。明天那班才是真的。"), new("周五，明天还要返场", "明天还得上班，今晚先别演大结局。"), new("周五这页有续集", "明天工位照常见，周五白长这张脸。")];
    private static readonly Copy[] RestWorkCopies =
    [new("休息日，工位临时加场", "今天本来休息，椅子倒是很高兴。"), new("休息日加班已排上", "床的意见暂时没被采纳。"), new("休息日也来工位", "日历写休息，计划写另一回事。")];
    private static readonly Copy[] WeekendWorkCopies =
    [new("周末工位返场", "周末没消失，只是我没参与。"), new("周末，照常上班", "别人出门，我也出门。目的地差点意思。"), new("周末工牌没放假", "日历是周末，今天的安排有自己的想法。")];
    private static readonly Copy[] MakeupCopies =
    [new("调休补班，回来了", "周末长得挺像，实际是工作日。"), new("补班，日历会拐弯", "放过的假，在这里等着呢。"), new("补班工位准时返场", "休的时候没细看，账单现在来了。")];
    private static readonly Copy[] UnknownCopies =
    [new("日历这页还没定", "先确认今天上不上，别让它瞎猜。"), new("今日类型待确认", "上班还是休息，这题得你答。"), new("日历暂时没主意", "确认一下类型，再决定工牌出不出门。")];
    private static readonly Copy[] HolidayRestCopies =
    [new("{节日}假期，工牌休眠", "闹钟响了也可以当没听见。"), new("{节日}放假，今日失联", "有事开工再说，没事也别找。"), new("{节日}假期，不用到岗", "床和沙发，今天轮流值班。")];
    private static readonly Copy[] HolidayWorkCopies =
    [new("{节日}假期，工位有我", "朋友圈都在看海，我在看表。"), new("{节日}假期仍需到岗", "别人堵在路上，我堵在待办里。"), new("{节日}假期，工牌返场", "景点人多，工位倒挺宽敞。")];
    private static readonly Copy[] HolidayEveCopies =
    [new("{节日}前，工位先收尾", "人还在这里，行李已经想走了。"), new("{节日}假期快到了", "新任务最好识趣一点。"), new("{节日}将至，心先请假", "剩下这点活，别突然长大。")];
    private static readonly Copy[] AfterHolidayCopies =
    [new("{节日}已结束，工位见", "假放完了，人还没缓过来。"), new("{节日}归来，工牌复活", "照片还没修完，待办先长出来了。"), new("{节日}假期已清零", "椅子认得我，我暂时不想认它。")];
    private static readonly Copy[] NearHolidayCopies =
    [new("{节日}在前面，班在眼前", "假期越近，表走得越有意见。"), new("{节日}快了，先把班上完", "攻略看好了，待办还没看完。"), new("{节日}将至，工牌别加戏", "想放假这件事，已经熟练掌握。")];
}
