using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace ToolsBox.Core.Lottery;

public sealed class LotteryNumbers
{
    public IReadOnlyList<int> Reds { get; }
    public int Blue { get; }
    [JsonConstructor]
    public LotteryNumbers(IReadOnlyList<int> reds, int blue)
    {
        if (reds is null || reds.Count != 6 || reds.Distinct().Count() != 6 || reds.Any(x => x is < 1 or > 33) || blue is < 1 or > 16)
            throw new ArgumentException("双色球号码必须为 6 个不重复的 1–33 红球和 1 个 1–16 蓝球。");
        Reds = Array.AsReadOnly(reds.OrderBy(x => x).ToArray()); Blue = blue;
    }
    public static LotteryNumbers Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length != 2) throw new FormatException("请输入六个红球 + 一个蓝球。");
        return new(parts[0].Split(new[] {' ', ',', '，', ';', '\t'}, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.Parse(x, NumberStyles.None, CultureInfo.InvariantCulture)).ToArray(),
            int.Parse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture));
    }
    public static IReadOnlyList<LotteryNumbers> MachinePick(int count)
    {
        LotteryRules.BatchCost(count, 1);
        return Enumerable.Range(0, count).Select(_ =>
        {
            var pool = Enumerable.Range(1, 33).ToArray();
            for (var i = 0; i < 6; i++) { var j = RandomNumberGenerator.GetInt32(i, 33); (pool[i], pool[j]) = (pool[j], pool[i]); }
            return new LotteryNumbers(pool.Take(6).ToArray(), RandomNumberGenerator.GetInt32(1, 17));
        }).ToArray();
    }
    public override string ToString() => string.Join(" ", Reds.Select(x => x.ToString("00", CultureInfo.InvariantCulture))) + " + " + Blue.ToString("00", CultureInfo.InvariantCulture);
}

public enum LotteryBuyMode { Real, Fake }
public sealed record LotteryDraw(string Issue, DateOnly DrawDate, LotteryNumbers Numbers,
    IReadOnlyDictionary<int, decimal> Payouts, bool? SpecialPrizeEnabled = null);
public sealed record LotteryPurchase(Guid Id, string Issue, LotteryNumbers Numbers, int Multiple, LotteryBuyMode Mode, DateTimeOffset RecordedAt)
{
    [JsonIgnore] public decimal Cost => LotteryRules.BatchCost(1, Multiple);
}
public sealed record LotteryFavorite(string Name, LotteryNumbers Numbers);
public sealed class LotteryState
{
    [JsonRequired]
    public List<LotteryDraw> Draws { get; set; } = new();
    [JsonRequired]
    public List<LotteryPurchase> Purchases { get; set; } = new();
    [JsonRequired]
    public List<LotteryFavorite> Favorites { get; set; } = new();
    public DateTimeOffset? LastUpdated { get; set; }
}
public sealed record LotterySettlement(LotteryPurchase Purchase, int? Tier, decimal? Prize, decimal? Net, string? PendingReason)
{
    public bool IsPending => PendingReason is not null;
}
public sealed record LotterySummary(decimal RealOutlay, decimal RealPrize, decimal RealSettledNet,
    decimal FakeStake, decimal FakePrize, decimal FakeSettledNet, int RealPendingCount, int FakePendingCount)
{
    public int PendingCount => RealPendingCount + FakePendingCount;
}
