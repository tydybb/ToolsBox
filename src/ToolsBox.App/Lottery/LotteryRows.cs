using ToolsBox.Core.Lottery;

namespace ToolsBox.App.Lottery;

public sealed record LotteryDrawRow(LotteryDraw Draw)
{
    public string Issue => Draw.Issue;
    public string Date => Draw.DrawDate.ToString("yyyy-MM-dd");
    public LotteryNumbers Numbers => Draw.Numbers;
    public string FirstPrize => Draw.Payouts.TryGetValue(1, out var p) ? p.ToString("N0") : "待公布";
    public string SecondPrize => Draw.Payouts.TryGetValue(2, out var p) ? p.ToString("N0") : "待公布";
    public string Special => Draw.SpecialPrizeEnabled switch { true => "启用", false => "未启用", _ => "待确认" };
}

public sealed record LotteryRecordRow(LotterySettlement Settlement, LotteryDraw? Draw)
{
    public string Issue => Settlement.Purchase.Issue;
    public string Mode => Settlement.Purchase.Mode == LotteryBuyMode.Real ? "真买" : "假买";
    public LotteryNumbers Numbers => Settlement.Purchase.Numbers;
    public int Multiple => Settlement.Purchase.Multiple;
    public decimal Cost => Settlement.Purchase.Cost;
    public string Match => Draw is null ? "—" : $"{Settlement.Purchase.Numbers.Reds.Intersect(Draw.Numbers.Reds).Count()} 红 + {(Settlement.Purchase.Numbers.Blue == Draw.Numbers.Blue ? 1 : 0)} 蓝";
    public string Result => Settlement.IsPending ? Settlement.PendingReason! : Settlement.Tier switch { null => "未中奖", 7 => "福运奖", var n => $"{n} 等奖" };
    public string Prize => Settlement.Prize?.ToString("N2") ?? "待结算";
    public string Net => Settlement.Net?.ToString("+0.00;-0.00;0.00") ?? "待结算";
    public string RecordedAt => Settlement.Purchase.RecordedAt.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm");
}
