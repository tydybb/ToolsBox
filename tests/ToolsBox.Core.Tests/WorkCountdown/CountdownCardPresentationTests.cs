using ToolsBox.Core.WorkCountdown;
using Card = ToolsBox.Core.WorkCountdown.CountdownCardPresentation;

namespace ToolsBox.Core.Tests.WorkCountdown;

public sealed class CountdownCardPresentationTests
{
    [Theory]
    [InlineData(2026, 10, 9, DayKind.Workday, true)]
    [InlineData(2026, 10, 10, DayKind.Workday, true)]
    [InlineData(2026, 10, 10, DayKind.RestDay, false)]
    [InlineData(2026, 10, 3, DayKind.Workday, true)]
    [InlineData(2026, 10, 3, DayKind.RestDay, false)]
    [InlineData(2027, 11, 8, DayKind.Workday, true)]
    [InlineData(2027, 11, 8, DayKind.Unknown, false)]
    public void FactText_UsesAppliedKindAndExistingCalendarDescription(
        int year, int month, int day, DayKind kind, bool hasTask)
    {
        DateOnly date = new(year, month, day);
        WorkSchedule? schedule = hasTask ? Schedule(date) : null;
        DateTime now = date.ToDateTime(new(10, 0));

        Card card = Select(new(date, kind, schedule, now, null, 0));

        Assert.StartsWith(WeekdayFunCopy.DayDescription(date, kind, hasTask, date), card.FactText);
    }

    [Theory]
    [InlineData(10, 8, "国庆节假期余额已清零")]
    [InlineData(9, 18, "距离中秋节假期还有 7 天")]
    [InlineData(10, 3, "假期余额还剩 4 天")]
    public void FactText_PreservesVisibleHolidayBalanceAndCountdown(int month, int day, string detail)
    {
        DateOnly date = new(2026, month, day);
        WorkSchedule schedule = Schedule(date);

        Assert.Contains(detail, Select(new(date, DayKind.Workday, schedule, schedule.Start, null, 0)).FactText);
    }

    [Fact]
    public void FridayBeforeMakeupSaturday_DoesNotPromiseAFreeWeekend()
    {
        DateOnly date = new(2026, 10, 9);
        WorkSchedule schedule = Schedule(date);
        Card card = Select(new(date, DayKind.Workday, schedule, schedule.Start, null, 0));

        Assert.Contains("明天调休补班", card.FactText);
        Assert.Contains("明天", card.Caption);
        Assert.DoesNotContain("48", card.Headline + card.Caption);
        Assert.DoesNotContain("周末自由", card.Headline + card.Caption);
        Assert.DoesNotContain("周末模式", card.Headline + card.Caption);
        Assert.DoesNotContain("连休", card.Headline + card.Caption);
    }

    [Fact]
    public void FridayWithKnownWorkTomorrow_HasThreeSpecificDailyVariations()
    {
        DateOnly[] dates = [new(2026, 2, 13), new(2026, 2, 27), new(2026, 5, 8), new(2026, 10, 9)];
        Card[] cards = dates.Select(date =>
        {
            WorkSchedule schedule = Schedule(date);
            return Select(new(date, DayKind.Workday, schedule, schedule.Start, null, 0));
        }).ToArray();

        Assert.All(cards, card => Assert.Contains("明天", card.Caption));
        Assert.True(cards.Select(card => card.Headline).Distinct().Count() >= 3);
        Assert.Contains(cards, card => card.Caption == "周五，仅供展示。明天那班才是真的。");
    }

    [Fact]
    public void ManualRestOnMakeupDay_UsesRestVoice()
    {
        DateOnly date = new(2026, 10, 10);
        Card card = Select(new(date, DayKind.RestDay, null, date.ToDateTime(new(10, 0)), null, 0));

        Assert.Contains("今天休息", card.FactText);
        Assert.Equal("rest", card.EffectKey);
        Assert.Equal("今日休息", card.StageLabel);
        Assert.DoesNotContain("补班", card.Headline + card.Caption);
    }

    [Theory]
    [InlineData(10, 3, "国庆节")]
    [InlineData(9, 25, "中秋节")]
    [InlineData(2, 17, "春节")]
    public void HolidayWithTask_UsesTheHolidayWorkVoice(int month, int day, string holiday)
    {
        DateOnly date = new(2026, month, day);
        WorkSchedule schedule = Schedule(date);
        Card card = Select(new(date, DayKind.Workday, schedule, schedule.Start, null, 0));

        Assert.Contains(holiday, card.Headline + card.Caption);
        Assert.Contains("假期上班", card.FactText);
        Assert.DoesNotContain("睡到自然醒", card.Headline + card.Caption);
    }

    [Fact]
    public void HolidayWithoutTask_UsesLeisureVoice()
    {
        DateOnly date = new(2026, 10, 3);
        Card card = Select(new(date, DayKind.RestDay, null, date.ToDateTime(new(10, 0)), null, 0));

        Assert.Contains("国庆节", card.Headline + card.Caption);
        Assert.Equal("rest", card.EffectKey);
        Assert.DoesNotContain("假期上班", card.Headline + card.Caption);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(12)]
    [InlineData(19)]
    public void RegularThursday_ExplicitlyUsesCrazyThursdayAndV50Memes(int day)
    {
        DateOnly date = new(2026, 11, day);
        WorkSchedule schedule = Schedule(date);
        Card card = Select(new(date, DayKind.Workday, schedule, schedule.Start, null, 0));
        string copy = card.Headline + card.Caption;

        Assert.Contains("疯狂星期四", copy);
        Assert.Contains("KFC", copy);
        Assert.Contains("V我50", copy);
        Assert.DoesNotContain("优惠价", copy);
        Assert.DoesNotContain("付款", copy);
        Assert.DoesNotContain("转账", copy);
    }

    [Theory]
    [InlineData(DayOfWeek.Monday)]
    [InlineData(DayOfWeek.Tuesday)]
    [InlineData(DayOfWeek.Wednesday)]
    [InlineData(DayOfWeek.Thursday)]
    [InlineData(DayOfWeek.Friday)]
    [InlineData(DayOfWeek.Saturday)]
    [InlineData(DayOfWeek.Sunday)]
    public void EachWeekday_HasAtLeastThreeStableDailyVariations(DayOfWeek weekday)
    {
        DateOnly first = new(2026, 11, 2);
        while (first.DayOfWeek != weekday) first = first.AddDays(1);
        var cards = Enumerable.Range(0, 6).Select(week =>
        {
            DateOnly date = first.AddDays(week * 7);
            WorkSchedule schedule = Schedule(date);
            return Select(new(date, DayKind.Workday, schedule, schedule.Start, null, 0));
        }).ToArray();

        Assert.True(cards.Select(card => card.Headline).Distinct().Count() >= 3);
        Assert.True(cards.Select(card => card.Caption).Distinct().Count() >= 3);
    }

    [Theory]
    [InlineData("rest", "rest", "今日休息")]
    [InlineData("ready", "idle", "尚未开工")]
    [InlineData("prestart", "idle", "等待开工")]
    [InlineData("fresh", "focus", "刚刚开工")]
    [InlineData("working2", "focus", "工作进行中")]
    [InlineData("working5", "focus", "注意续电")]
    [InlineData("last30", "rush", "最后半小时")]
    [InlineData("overtime", "overtime", "计划加班中")]
    [InlineData("overtime2", "overtime", "加班进行中")]
    [InlineData("overtime5", "overtime", "加班较久")]
    [InlineData("unpaid", "overdue", "计划已结束")]
    [InlineData("unreasonable", "overdue", "计划过长")]
    [InlineData("finished", "celebrate", "已经下班")]
    [InlineData("early", "celebrate", "提前收工")]
    public void MeaningfulStage_HasCorrectEffectAndThreeShortBubbleVariations(
        string stage, string effect, string label)
    {
        Card[] cards = Enumerable.Range(0, 9)
            .Select(week => Select(ContextForStage(new DateOnly(2026, 11, 9).AddDays(week * 7), stage)))
            .ToArray();

        Assert.All(cards, card =>
        {
            Assert.Equal(effect, card.EffectKey);
            Assert.Equal(label, card.StageLabel);
            Assert.InRange(card.SpeechText.Length, 1, 20);
            Assert.False(string.IsNullOrWhiteSpace(card.Headline));
            Assert.False(string.IsNullOrWhiteSpace(card.Caption));
            Assert.DoesNotContain("实际工作", card.Caption);
            Assert.DoesNotContain("已经工作", card.Caption);
        });
        Assert.True(cards.Select(card => card.SpeechText).Distinct().Count() >= 3);
    }

    [Theory]
    [InlineData(1, 59, 59, "刚刚开工")]
    [InlineData(2, 0, 0, "工作进行中")]
    [InlineData(4, 59, 59, "工作进行中")]
    [InlineData(5, 0, 0, "注意续电")]
    public void WorkingStages_UseExistingElapsedTimeBoundaries(
        int hours, int minutes, int seconds, string label)
    {
        DateOnly date = new(2026, 11, 9);
        WorkSchedule schedule = Schedule(date) with { ExcludedBreaks = TimeSpan.FromHours(3) };
        DateTime now = schedule.Start.AddHours(hours).AddMinutes(minutes).AddSeconds(seconds);

        Assert.Equal(label, Select(new(date, DayKind.Workday, schedule, now, null, 0)).StageLabel);
    }

    [Theory]
    [InlineData(30, 1, "focus")]
    [InlineData(30, 0, "rush")]
    [InlineData(0, 1, "rush")]
    [InlineData(0, 0, "overdue")]
    public void LastThirtyMinutes_AreInclusiveButPlanEndHasPriority(int minutes, int seconds, string effect)
    {
        DateOnly date = new(2026, 11, 9);
        WorkSchedule schedule = Schedule(date);

        Assert.Equal(effect, Select(new(date, DayKind.Workday, schedule,
            schedule.End.AddMinutes(-minutes).AddSeconds(-seconds), null, 0)).EffectKey);
    }

    [Theory]
    [InlineData(DayKind.Workday, 14, "等待开工")]
    [InlineData(DayKind.Workday, 15, "计划过长")]
    [InlineData(DayKind.RestDay, 21, "等待开工")]
    [InlineData(DayKind.RestDay, 22, "计划过长")]
    public void UnreasonablePlan_UsesKindSpecificThresholdBeforeStart(DayKind kind, int hours, string label)
    {
        DateOnly date = new(2026, 11, 9);
        WorkSchedule schedule = Schedule(date, hours, kind);

        Assert.Equal(label, Select(new(date, kind, schedule, schedule.Start.AddSeconds(-1), null, hours)).StageLabel);
    }

    [Fact]
    public void UnpaidStage_WinsOverUnreasonablePlan()
    {
        CountdownReactionContext context = ContextForStage(new(2026, 11, 9), "unreasonable");
        Card card = Select(context with { Now = context.Schedule!.End });

        Assert.Equal("计划已结束", card.StageLabel);
        Assert.Contains("无偿加班", card.Caption);
    }

    [Theory]
    [InlineData("early")]
    [InlineData("finished")]
    public void FinishedStage_WinsOverStressAndFreezesAllPresentation(string stage)
    {
        CountdownReactionContext context = ContextForStage(new(2026, 11, 13), stage);
        Card card = Select(context);
        Card later = Select(context with { Now = context.Now.AddDays(7), OvertimeHours = 99 });

        Assert.Equal("celebrate", card.EffectKey);
        Assert.Equal(card, later);
        Assert.DoesNotContain("无偿加班", card.Caption);
    }

    [Fact]
    public void CrossMidnight_FactUsesActualTomorrowAndKeepsTaskDate()
    {
        DateOnly date = new(2026, 10, 9);
        WorkSchedule schedule = Schedule(date, 8);
        DateTime now = new(2026, 10, 10, 0, 30, 0);
        Card card = Select(new(date, DayKind.Workday, schedule, now, null, 8));

        Assert.Contains("2026-10-09 周五任务", card.FactText);
        Assert.Contains("明天休息", card.FactText);
        Assert.DoesNotContain("明天调休补班", card.FactText + card.Caption + card.Headline);
        Assert.Equal(card, Select(new(date, DayKind.Workday, schedule, now.AddSeconds(1), null, 8)));
    }

    [Fact]
    public void FinishedAcrossMidnight_FactUsesFrozenFinishDate()
    {
        DateOnly date = new(2026, 10, 9);
        WorkSchedule schedule = Schedule(date, 8);
        DateTime finished = new(2026, 10, 10, 2, 0, 0);
        var context = new CountdownReactionContext(date, DayKind.Workday, schedule, finished, finished, 8);

        Card card = Select(context);

        Assert.Contains("2026-10-09 周五任务", card.FactText);
        Assert.Contains("明天休息", card.FactText);
        Assert.Equal(card, Select(context with { Now = finished.AddDays(7) }));
    }

    [Fact]
    public void WithinAStage_CopyDoesNotChurnAsTheClockTicks()
    {
        CountdownReactionContext context = ContextForStage(new(2026, 11, 9), "working2");

        Assert.Equal(Select(context), Select(context with { Now = context.Now.AddMinutes(30).AddSeconds(17) }));
    }

    [Fact]
    public void AllCalendarAndStageCopies_AreOneShortPairWithoutCharacterNarration()
    {
        string[] stages = ["rest", "ready", "prestart", "fresh", "working2", "working5", "last30",
            "overtime", "overtime2", "overtime5", "unpaid", "unreasonable", "finished", "early"];
        DateOnly first = new(2026, 1, 1);
        string[] canned = ["陪你", "替你", "提醒你", "照顾好自己", "给自己续电", "小快乐"];

        foreach (DateOnly date in Enumerable.Range(0, 365).Select(first.AddDays))
        foreach (string stage in stages)
        {
            Card card = Select(ContextForStage(date, stage));
            Assert.InRange(card.Headline.Length, 1, 20);
            Assert.InRange(card.Caption.Length, 1, 36);
            Assert.InRange(card.SpeechText.Length, 1, 20);
            Assert.DoesNotContain(" · ", card.Headline);
            Assert.DoesNotContain("{", card.Headline + card.Caption + card.SpeechText);
            foreach (string template in canned)
                Assert.DoesNotContain(template, card.Headline + card.Caption + card.SpeechText);
            foreach (string character in new[] { "厘普", "多栋", "秀珍", "田中", "小河" })
                Assert.DoesNotContain(character, card.Caption);
        }
    }

    [Theory]
    [InlineData("last30", "椅子，你先适应一下没有我。")]
    [InlineData("unpaid", "下班时间都走了，怎么就你不走？")]
    [InlineData("unreasonable", "先别算加班了，算算房租。")]
    public void ApprovedStageJokes_AppearInTheirMatchingStage(string stage, string caption)
    {
        Assert.Contains(Enumerable.Range(0, 9)
            .Select(week => Select(ContextForStage(new DateOnly(2026, 11, 9).AddDays(week * 7), stage))),
            card => card.Caption.Contains(caption, StringComparison.Ordinal));
    }

    [Fact]
    public void ApprovedCalendarJokes_AppearInMatchingDays()
    {
        Card[] mondays = Enumerable.Range(0, 6).Select(week => new DateOnly(2026, 11, 2).AddDays(week * 7))
            .Select(date => Select(ContextForStage(date, "fresh"))).ToArray();
        Card[] thursdays = Enumerable.Range(0, 6).Select(week => new DateOnly(2026, 11, 5).AddDays(week * 7))
            .Select(date => Select(ContextForStage(date, "fresh"))).ToArray();
        Card[] holidayTasks = Enumerable.Range(0, 7).Select(day => new DateOnly(2026, 10, 1).AddDays(day))
            .Select(date => Select(ContextForStage(date, "fresh"))).ToArray();

        Assert.Contains(mondays, card => card.Caption == "星期这么大了，应该学会自己上班。");
        Assert.Contains(thursdays, card => card.Caption.Contains("鸡块下锅了，我还在工位腌着。", StringComparison.Ordinal));
        Assert.Contains(thursdays, card => card.Caption == "V我50分钟，给下班办个加急。");
        Assert.Contains(holidayTasks, card => card.Caption == "朋友圈都在看海，我在看表。");
    }

    [Theory]
    [InlineData("unpaid", "无偿加班")]
    [InlineData("unreasonable", "计划过长")]
    [InlineData("finished", "下班")]
    [InlineData("early", "提前")]
    public void SpecialStageCopy_WinsOverHolidayAndWeekdayOpeners(string stage, string expected)
    {
        Card card = Select(ContextForStage(new(2026, 10, 3), stage));

        Assert.Contains(expected, card.Headline + card.Caption);
        Assert.DoesNotContain("国庆", card.Headline + card.Caption);
        Assert.Contains("国庆", card.FactText);
    }

    [Fact]
    public void SpeechUsesTheCharacterDialogueAndTheExistingStableSeed()
    {
        CountdownReactionContext context = ContextForStage(new(2026, 11, 9), "fresh");
        Card card = Select(context);
        Type? dialogue = typeof(Card).Assembly.GetType("ToolsBox.Core.WorkCountdown.CountdownCharacterDialogue");
        Assert.NotNull(dialogue);
        string? speech = dialogue.GetMethod("Stage")!.Invoke(null, ["厘普", "focus", context.WorkDate.DayNumber + 3 * 7]) as string;

        Assert.Equal(speech, card.SpeechText);
    }

    [Theory]
    [InlineData(2027, 11, 12)]
    [InlineData(2026, 12, 31)]
    [InlineData(9999, 12, 31)]
    public void MissingNextYearAndMaxDate_DoNotInventTomorrowOff(int year, int month, int day)
    {
        DateOnly date = new(year, month, day);
        Card card = Select(new(date, DayKind.RestDay, null, date.ToDateTime(new(10, 0)), null, 0));

        Assert.DoesNotContain("明天休息", card.FactText + card.Headline + card.Caption);
        Assert.DoesNotContain("明天放假", card.FactText + card.Headline + card.Caption);
        Assert.DoesNotContain("48", card.Headline + card.Caption);
    }

    [Fact]
    public void RestDayTask_UsesOvertimeBeforeTheFinalHalfHour()
    {
        DateOnly date = new(2026, 11, 14);
        WorkSchedule schedule = Schedule(date, 4, DayKind.RestDay);
        Card card = Select(new(date, DayKind.RestDay, schedule, schedule.End.AddMinutes(-1), null, 4));

        Assert.Equal("overtime", card.EffectKey);
        Assert.Contains("休息日加班", card.FactText);
    }

    private static CountdownReactionContext ContextForStage(DateOnly date, string stage)
    {
        if (stage is "rest" or "ready")
            return new(date, stage == "rest" ? DayKind.RestDay : DayKind.Workday,
                null, date.ToDateTime(new(10, 0)), null, 0);
        int overtime = stage.StartsWith("overtime") ? 8 : stage is "unreasonable" or "finished" or "early" ? 15 : 0;
        WorkSchedule schedule = Schedule(date, overtime);
        DateTime now = stage switch
        {
            "prestart" or "unreasonable" => schedule.Start.AddSeconds(-1),
            "working2" => schedule.Start.AddHours(2),
            "working5" => schedule.Start.AddHours(5),
            "last30" => schedule.End.AddMinutes(-30),
            "overtime" => schedule.NormalEnd!.Value,
            "overtime2" => schedule.NormalEnd!.Value.AddHours(2),
            "overtime5" => schedule.NormalEnd!.Value.AddHours(5),
            "unpaid" or "finished" => schedule.End,
            "early" => schedule.NormalEnd!.Value.AddSeconds(-1),
            _ => schedule.Start
        };
        return new(date, DayKind.Workday, schedule, now, stage is "early" or "finished" ? now : null, overtime);
    }

    private static WorkSchedule Schedule(DateOnly date, int overtime = 0, DayKind kind = DayKind.Workday) =>
        CountdownCalculator.Calculate(date, new(8, 30), kind, overtime);

    private static Card Select(CountdownReactionContext context) => CountdownCardPresentationSelector.Select(context);
}
