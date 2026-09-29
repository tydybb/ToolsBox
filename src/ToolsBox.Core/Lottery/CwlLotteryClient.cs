using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ToolsBox.Core.Lottery;

public interface ILotteryClient
{
    Task<IReadOnlyList<LotteryDraw>> FetchAsync(bool allHistory, IProgress<string>? progress, CancellationToken cancellationToken);
}

public sealed record CwlRuleTransition(string Issue, bool Enabled);
public sealed record CwlPage(IReadOnlyList<LotteryDraw> Draws, int PageCount, IReadOnlyList<CwlRuleTransition> Transitions, int PageNo = 1);

/// <summary>只读公开开奖数据，不涉及在线购票。批次全部成功才返回。</summary>
public sealed class CwlLotteryClient : ILotteryClient
{
    public const string Source = "https://www.cwl.gov.cn/ygkj/wqkjgg/ssq/";
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly HttpClient _http;
    private readonly TimeSpan _requestTimeout;
    public CwlLotteryClient(HttpClient? http = null, TimeSpan? requestTimeout = null)
    {
        _http = http ?? SharedClient;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(25);
        if (_requestTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
    }

    public async Task<IReadOnlyList<LotteryDraw>> FetchAsync(bool allHistory, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var pages = new List<CwlPage>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int count = 1;
        for (int number = 1; number <= count; number++)
        {
            progress?.Report($"正在获取官方开奖数据：第 {number}/{count} 页…");
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://www.cwl.gov.cn/cwl_admin/front/cwlkj/search/kjxx/findDrawNotice?name=ssq&pageNo={number}&pageSize=100&systemType=PC");
            request.Headers.Referrer = new Uri(Source);
            request.Headers.UserAgent.ParseAdd("ToolsBox/1.0");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_requestTimeout);
            var token = timeout.Token;
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            const int maxBytes = 4 * 1024 * 1024;
            await using var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await body.ReadAsync(chunk.AsMemory(), token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + read > maxBytes) throw new InvalidDataException("官方响应超过 4 MiB 大小限制。");
                buffer.Write(chunk, 0, read);
            }
            token.ThrowIfCancellationRequested();
            var page = ParsePage(System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
            if (page.PageNo != number || (pages.Count > 0 && page.PageCount != pages[0].PageCount))
                throw new InvalidDataException("官方分页编号或总页数发生变化，请稍后重试。");
            if (page.Draws.Any(x => !seen.Add(x.Issue)))
                throw new InvalidDataException("官方分页包含重复期次，可能正在更新开奖，请稍后重试。");
            pages.Add(page);
            if (number == 1 && allHistory) count = page.PageCount;
        }
        return ResolveSpecialRules(pages);
    }

    public static CwlPage ParsePage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("state", out var state) || state.GetInt32() != 0)
            throw new InvalidDataException("官方接口未返回成功结果。");
        var result = root.GetProperty("result");
        if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0 || result.GetArrayLength() > 100)
            throw new InvalidDataException("官方开奖列表为空或格式发生变化。");
        int pageCount = root.TryGetProperty("pageNum", out var pageNum) ? pageNum.GetInt32() : 1;
        if (pageCount is < 1 or > 200) throw new InvalidDataException("历史数据页数异常。");
        if (!root.TryGetProperty("pageNo", out var pageNoValue) || !pageNoValue.TryGetInt32(out var pageNo) || pageNo < 1 || pageNo > pageCount)
            throw new InvalidDataException("官方分页编号缺失或异常。");
        var draws = new List<LotteryDraw>();
        var transitions = new List<CwlRuleTransition>();
        foreach (var row in result.EnumerateArray())
        {
            string issue = Text(row, "code");
            if (!LotteryRules.IsValidIssue(issue)) throw new InvalidDataException("开奖期号格式异常。");
            string dateText = Text(row, "date");
            if (dateText.Length < 10 || !DateOnly.TryParseExact(dateText[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new InvalidDataException("开奖日期格式异常。");
            var numbers = LotteryNumbers.Parse(Text(row, "red") + " + " + Text(row, "blue"));
            var payouts = new Dictionary<int, decimal>();
            if (row.TryGetProperty("prizegrades", out var grades) && grades.ValueKind == JsonValueKind.Array)
            {
                foreach (var grade in grades.EnumerateArray())
                {
                    int tier = grade.GetProperty("type").GetInt32();
                    if (tier is >= 1 and <= 7 && Money(Text(grade, "typemoney")) is { } amount)
                    {
                        if (!payouts.TryAdd(tier, amount)) throw new InvalidDataException("开奖奖金含重复奖级。");
                    }
                }
            }
            bool? special = null;
            if (Money(Text(row, "fyjMoney")) is { } fuyun)
            {
                payouts[7] = fuyun;
                special = true;
            }
            else if (payouts.ContainsKey(7)) special = true;
            foreach (Match match in Regex.Matches(Text(row, "specialRuleInfo"), @"自([0-9]{7})期[^。]*?起(不执行|执行)特别规定"))
                transitions.Add(new(match.Groups[1].Value, match.Groups[2].Value == "执行"));
            // 旧规则尚无福运奖；新规则未知时不能把三红零蓝直接判为未中奖。
            if (string.CompareOrdinal(issue, "2026014") < 0) special = false;
            var draw = new LotteryDraw(issue, date, numbers, payouts, special);
            LotteryRules.ValidateDraw(draw);
            draws.Add(draw);
        }
        if (draws.Select(x => x.Issue).Distinct().Count() != draws.Count) throw new InvalidDataException("开奖期次重复。");
        return new(draws, pageCount, transitions, pageNo);
    }

    public static IReadOnlyList<LotteryDraw> ResolveSpecialRules(IEnumerable<CwlPage> pages)
    {
        var array = pages.ToArray();
        var transitions = array.SelectMany(x => x.Transitions).OrderBy(x => x.Issue, StringComparer.Ordinal).ToArray();
        return array.SelectMany(x => x.Draws).Select(draw =>
        {
            if (draw.SpecialPrizeEnabled.HasValue) return draw;
            var transition = transitions.LastOrDefault(x => string.CompareOrdinal(x.Issue, draw.Issue) <= 0);
            return transition is null ? draw : draw with { SpecialPrizeEnabled = transition.Enabled };
        }).OrderByDescending(x => x.Issue, StringComparer.Ordinal).ToArray();
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static decimal? Money(string text) => decimal.TryParse(text.Replace(",", ""), NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture, out var amount) && amount > 0 && amount <= 100_000_000m ? amount : null;
}
