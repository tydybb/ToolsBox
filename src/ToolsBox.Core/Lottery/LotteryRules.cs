using System.Globalization;

namespace ToolsBox.Core.Lottery;

public static class LotteryRules
{
    public static bool IsValidIssue(string? issue) => issue is {Length:7} && issue.All(c => c is >= '0' and <= '9')
        && int.Parse(issue[..4], CultureInfo.InvariantCulture) is >= 2003 and <= 9999
        && int.Parse(issue[4..], CultureInfo.InvariantCulture) is >= 1 and <= 999;

    public static decimal BatchCost(int count, int multiple)
    {
        if (count < 1 || multiple is < 1 or > 99 || (long)count * multiple * 2 > 20000)
            throw new ArgumentOutOfRangeException(nameof(count), "每注 2 元，倍数 1–99，单批金额不得超过 20000 元。");
        return 2m * count * multiple;
    }
    public static LotterySettlement Settle(LotteryPurchase purchase, LotteryDraw? draw)
    {
        ValidatePurchase(purchase);
        if (draw is null || draw.Issue != purchase.Issue) return new(purchase,null,null,null,"等待本期开奖");
        ValidateDraw(draw);
        int reds = purchase.Numbers.Reds.Intersect(draw.Numbers.Reds).Count(); bool blue = purchase.Numbers.Blue == draw.Numbers.Blue;
        int? tier = (reds, blue) switch { (6,true)=>1,(6,false)=>2,(5,true)=>3,(5,false) or (4,true)=>4,
            (4,false) or (3,true)=>5,(_,true)=>6,(3,false) when draw.SpecialPrizeEnabled == true=>7,_=>null };
        if (reds == 3 && !blue && draw.SpecialPrizeEnabled is null) return new(purchase,null,null,null,"等待官方确认特别奖是否启用");
        decimal prize = 0;
        if (tier is int level)
        {
            if (!draw.Payouts.TryGetValue(level, out var amount) || amount <= 0) return new(purchase,tier,null,null,"等待官方单注奖金");
            prize = amount * purchase.Multiple;
        }
        return new(purchase,tier,prize,purchase.Mode == LotteryBuyMode.Real ? prize-purchase.Cost : purchase.Cost-prize,null);
    }
    public static LotterySummary Summarize(LotteryState state)
    {
        var results = state.Purchases.Select(p => Settle(p, state.Draws.FirstOrDefault(d=>d.Issue==p.Issue))).ToArray();
        var real=results.Where(x=>x.Purchase.Mode==LotteryBuyMode.Real).ToArray(); var fake=results.Where(x=>x.Purchase.Mode==LotteryBuyMode.Fake).ToArray();
        return new(real.Sum(x=>x.Purchase.Cost),real.Sum(x=>x.Prize??0),real.Sum(x=>x.Net??0),
            fake.Sum(x=>x.Purchase.Cost),fake.Sum(x=>x.Prize??0),fake.Sum(x=>x.Net??0),real.Count(x=>x.IsPending),fake.Count(x=>x.IsPending));
    }
    public static DateOnly NextRegularDrawDate(DateOnly date)
    {
        do {date=date.AddDays(1);} while(date.DayOfWeek is not (DayOfWeek.Tuesday or DayOfWeek.Thursday or DayOfWeek.Sunday));
        return date;
    }
    public static string? BuyGuard(string issue, DateOnly drawDate, LotteryDraw latest, IEnumerable<LotteryDraw> knownDraws, DateTimeOffset now)
    {
        ValidateDraw(latest);
        if (!IsValidIssue(issue) || issue[..4]!=latest.Issue[..4] || int.Parse(issue,CultureInfo.InvariantCulture)!=int.Parse(latest.Issue,CultureInfo.InvariantCulture)+1)
            return "只能记录最新已公布期号的下一期；跨年期号须等待官方确认。";
        if (knownDraws.Any(x=>x.Issue==issue)) return "该期已经开奖，不能补录投注。";
        var local = now.ToOffset(TimeSpan.FromHours(8));
        if (drawDate!=NextRegularDrawDate(latest.DrawDate) || drawDate.Year!=latest.DrawDate.Year || drawDate.DayNumber-latest.DrawDate.DayNumber>7
            || DateOnly.FromDateTime(local.DateTime).DayNumber-latest.DrawDate.DayNumber>7)
            return "开奖日期或缓存时效无法确认，请刷新并核对官方销售公告（节假日休市可能调整）。";
        var cutoff=new DateTimeOffset(drawDate.ToDateTime(new TimeOnly(20,0)),TimeSpan.FromHours(8));
        if (now>=cutoff) return "已达到北京时间 20:00 的保守截止时间，不能记录本期投注。";
        if (local.Date < latest.DrawDate.ToDateTime(TimeOnly.MinValue)) return "系统时间早于最新开奖日期。";
        return null;
    }
    internal static void ValidatePurchase(LotteryPurchase p)
    {
        if (p is null || p.Id==Guid.Empty || !IsValidIssue(p.Issue) || p.Numbers is null || !Enum.IsDefined(p.Mode) || p.RecordedAt==default)
            throw new ArgumentException("投注记录无效。");
        BatchCost(1,p.Multiple);
    }
    internal static void ValidateDraw(LotteryDraw d)
    {
        if (d is null || !IsValidIssue(d.Issue) || d.Numbers is null || d.DrawDate==default || d.DrawDate.Year.ToString(CultureInfo.InvariantCulture)!=d.Issue[..4]
            || d.Payouts is null || d.Payouts.Any(x=>x.Key is <1 or >7 || x.Value<0)) throw new ArgumentException("开奖记录无效。");
    }
}
