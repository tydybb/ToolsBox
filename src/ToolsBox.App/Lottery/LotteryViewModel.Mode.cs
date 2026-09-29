using System.Globalization;
using ToolsBox.Core.Lottery;

namespace ToolsBox.App.Lottery;

public sealed partial class LotteryViewModel
{
    public async Task ToggleModeAsync(Guid purchaseId)
    {
        if (!CanEdit) return;
        Busy(true);
        try
        {
            var purchase = _state.Purchases.SingleOrDefault(p => p.Id == purchaseId)
                ?? throw new InvalidOperationException("该记录已不存在，请刷新列表。");
            EnsureModeChangeAllowed(purchase);
            var nextMode = purchase.Mode == LotteryBuyMode.Real ? LotteryBuyMode.Fake : LotteryBuyMode.Real;
            string label = nextMode == LotteryBuyMode.Real ? "真买" : "假买";
            if (!_confirm($"将第 {purchase.Issue} 期这一注切换为“{label}”？\n本金 {purchase.Cost:N2} 元，实际总投入会随之调整。\n仅修改记账模式，不会购买或退款；号码、倍数和期号不变。")) return;

            // 确认窗口可能停留很久；必须联网核对并再次校验时间，不能依赖点击前的旧行。
            await FetchAndSaveAsync(false);
            _lifetime.Token.ThrowIfCancellationRequested();
            EnsureModeChangeAllowed(purchase);
            Save(NewState(purchases: _state.Purchases.Select(p => p.Id == purchaseId ? p with { Mode = nextMode } : p).ToList()));
            Status = $"已切换为{label}并保存，投入和统计已重新计算。";
        }
        catch (OperationCanceledException) { Status = "模式修改已取消或联网超时，原模式保留。"; }
        catch (Exception ex) { Status = "模式修改失败，原模式保留：" + ex.Message; }
        finally { Busy(false); OnPropertyChanged(nameof(NextDrawStatus)); }
    }

    private void EnsureModeChangeAllowed(LotteryPurchase purchase)
    {
        if (_state.Draws.Any(d => string.CompareOrdinal(d.Issue, purchase.Issue) >= 0))
            throw new InvalidOperationException("该期已开奖，不能修改模式（奖金待公布也不允许）。");
        var latest = _state.Draws.MaxBy(d => d.Issue, StringComparer.Ordinal);
        if (latest is null || latest.Issue[..4] != purchase.Issue[..4] ||
            int.Parse(purchase.Issue, CultureInfo.InvariantCulture) != int.Parse(latest.Issue, CultureInfo.InvariantCulture) + 1)
            throw new InvalidOperationException("无法确认该期尚未开奖，请先刷新官方数据。");
        var date = LotteryRules.NextRegularDrawDate(latest.DrawDate);
        var drawTime = new DateTimeOffset(date.ToDateTime(new TimeOnly(21, 15)), TimeSpan.FromHours(8));
        var now = _now();
        if (date.Year != latest.DrawDate.Year || now >= drawTime || DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(8)).DateTime) < latest.DrawDate)
            throw new InvalidOperationException("已到预计开奖时间或期次时间无法确认，模式已锁定，请刷新。");
    }
}
