using System.Globalization;
using System.IO;
using ToolsBox.Core.Lottery;

namespace ToolsBox.App.Lottery;

public sealed partial class LotteryViewModel
{
    public async Task RefreshAsync(bool allHistory = false)
    {
        if (!CanEdit) return;
        Busy(true);
        try { await FetchAndSaveAsync(allHistory); Status = $"刷新成功，缓存 {Draws.Count} 期，已重新核对所有买入记录。"; }
        catch (OperationCanceledException) { Status = "请求已取消或超时，保留原记录。"; }
        catch (Exception ex) { Status = "刷新失败，保留原记录：" + ex.Message; }
        finally { Busy(false); OnPropertyChanged(nameof(NextDrawStatus)); }
    }

    public async Task BuyAsync()
    {
        if (!CanEdit || Draft.Count == 0) return;
        Busy(true);
        try
        {
            if (!ScheduleConfirmed) throw new ArgumentException("请核对期次和预计开奖日，并勾选确认。");
            var latest = _state.Draws.MaxBy(x => x.Issue, StringComparer.Ordinal) ?? throw new ArgumentException("请先刷新开奖数据。");
            string issue = NextIssue;
            var date = LotteryRules.NextRegularDrawDate(latest.DrawDate);
            int multiple = Integer(MultipleInput, "倍数");
            decimal cost = LotteryRules.BatchCost(Draft.Count, multiple);
            var selected = Draft.ToArray();
            var mode = IsRealBuy ? LotteryBuyMode.Real : LotteryBuyMode.Fake;
            string modeText = mode == LotteryBuyMode.Real ? "真买：记录已线下购买的实际支出，不会在线购票" : "假买：不花钱；未中奖记省下本金，中奖按本金减奖金记机会盈亏";
            if (!_confirm($"第 {issue} 期，预计 {date:yyyy-MM-dd}\n{selected.Length} 注 × {multiple} 倍，共 {cost:N2} 元。\n{modeText}\n\n保存后号码、倍数、期号不可修改或删除；未开奖时可双击模式切换真假买。是否保存？")) return;
            await FetchAndSaveAsync(false);
            latest = _state.Draws.MaxBy(x => x.Issue, StringComparer.Ordinal)!;
            string? error = LotteryRules.BuyGuard(issue, date, latest, _state.Draws, _now());
            if (error is not null) throw new ArgumentException(error);
            var purchases = selected.Select(n => new LotteryPurchase(Guid.NewGuid(), issue, n, multiple, mode, _now())).ToList();
            Save(NewState(purchases: [.. _state.Purchases, .. purchases]));
            Draft.Clear(); ScheduleConfirmed = false;
            Status = $"已保存 {purchases.Count} 注{(mode == LotteryBuyMode.Real ? "真买" : "假买")}记录。开奖后刷新自动核对。";
        }
        catch (OperationCanceledException) { Status = "保存取消或请求超时，选号保留，请重试。"; }
        catch (Exception ex) { Status = "买入记录保存失败，选号保留：" + ex.Message; }
        finally { Busy(false); OnPropertyChanged(nameof(NextDrawStatus)); }
    }

    private async Task FetchAndSaveAsync(bool allHistory)
    {
        var incoming = await _client.FetchAsync(allHistory, new Progress<string>(s => Status = s), _lifetime.Token);
        _lifetime.Token.ThrowIfCancellationRequested();
        if (incoming.Count == 0) throw new InvalidDataException("未获取到有效开奖，禁止保存买入。");
        string oldest = incoming.Min(x => x.Issue)!;
        bool needsCatchUp = _state.Purchases.Any(p => string.CompareOrdinal(p.Issue, oldest) < 0
            && LotteryRules.Settle(p, _state.Draws.FirstOrDefault(d => d.Issue == p.Issue)).IsPending);
        if (!allHistory && (needsCatchUp || incoming.Any(d => d.SpecialPrizeEnabled is null)))
        {
            Status = "正在补齐较早的待结算期次及特别规则公告…";
            incoming = await _client.FetchAsync(true, new Progress<string>(s => Status = s), _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            if (incoming.Count == 0) throw new InvalidDataException("历史补齐未返回数据，保留原记录。");
        }
        var map = _state.Draws.ToDictionary(d => d.Issue, StringComparer.Ordinal);
        foreach (var draw in incoming)
        {
            if (map.TryGetValue(draw.Issue, out var cached) && cached.Numbers.ToString() == draw.Numbers.ToString())
                map[draw.Issue] = draw with { Payouts = cached.Payouts.Concat(draw.Payouts).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Last().Value),
                    SpecialPrizeEnabled = draw.SpecialPrizeEnabled ?? cached.SpecialPrizeEnabled };
            else map[draw.Issue] = draw;
        }
        var next = NewState(draws: map.Values.OrderByDescending(d => d.Issue, StringComparer.Ordinal).ToList());
        next.LastUpdated = _now(); Save(next);
    }

    private LotteryState NewState(List<LotteryDraw>? draws = null, List<LotteryPurchase>? purchases = null, List<LotteryFavorite>? favorites = null) =>
        new() { Draws = draws ?? [.. _state.Draws], Purchases = purchases ?? [.. _state.Purchases], Favorites = favorites ?? [.. _state.Favorites], LastUpdated = _state.LastUpdated };
    private void Save(LotteryState next) { _store.Save(next); _state = next; UpdateRows(); }
    private void UpdateRows()
    {
        Favorites.Clear(); foreach (var f in _state.Favorites) Favorites.Add(f);
        Draws.Clear(); foreach (var d in _state.Draws.OrderByDescending(x => x.Issue, StringComparer.Ordinal)) Draws.Add(new(d));
        Records.Clear(); foreach (var p in _state.Purchases.OrderByDescending(x => x.RecordedAt))
            Records.Add(new(LotteryRules.Settle(p, _state.Draws.FirstOrDefault(d => d.Issue == p.Issue)), _state.Draws.FirstOrDefault(d => d.Issue == p.Issue)));
        var latest = _state.Draws.MaxBy(x => x.Issue, StringComparer.Ordinal);
        string oldIssue = NextIssue;
        if (latest is not null)
        {
            NextIssue = (int.Parse(latest.Issue, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
            NextDate = LotteryRules.NextRegularDrawDate(latest.DrawDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        if (oldIssue != NextIssue) ScheduleConfirmed = false;
        SourceStatus = $"来源：中国福彩网 · 缓存 {_state.Draws.Count} 期 · 更新时间：{_state.LastUpdated?.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss") ?? "未更新"}（北京时间）";
        var s = LotteryRules.Summarize(_state);
        TotalNet = s.RealSettledNet + s.FakeSettledNet;
        TotalSummary = $"总盈亏（含假买）：{TotalNet:+0.00;-0.00;0.00} 元\n真买已结算净盈亏 + 假买已结算机会盈亏 · 非现金收益 · 待结算 {s.PendingCount} 注不计入";
        RealSummary = $"真买 · 实际总投入 {s.RealOutlay:N2} 元 / 已知税前奖金 {s.RealPrize:N2} 元\n已结算净盈亏 {s.RealSettledNet:+0.00;-0.00;0.00} 元 · 待结算 {s.RealPendingCount} 注（待结算本金暂不计入净盈亏）";
        FakeSummary = $"假买 · 实际投入 0 元 / 模拟总本金 {s.FakeStake:N2} 元 / 应得税前奖金 {s.FakePrize:N2} 元\n已结算机会盈亏 {s.FakeSettledNet:+0.00;-0.00;0.00} 元 · 待结算 {s.FakePendingCount} 注（不提前计省下本金）";
        foreach (string name in new[] { nameof(NextIssue), nameof(NextDate), nameof(SourceStatus), nameof(NextDrawStatus), nameof(RealSummary), nameof(FakeSummary), nameof(TotalNet), nameof(TotalSummary) }) OnPropertyChanged(name);
    }
}
