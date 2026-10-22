using ToolsBox.Core.Lottery;

namespace ToolsBox.Core.Tests.Lottery;

public sealed class CwlLotteryClientTests
{
    [Theory]
    [InlineData("0000112")]
    [InlineData("2026000")]
    [InlineData("2025112")]
    public void InvalidIssueOrDateYearRejectsPage(string issue) =>
        Assert.ThrowsAny<Exception>(() => CwlLotteryClient.ParsePage(Page(issue, "", "")));

    [Fact]
    public async Task TimeoutCancelsStalledResponseBody()
    {
        var stream = new StalledStream();
        using var http = new HttpClient(new Handler(_ => new StreamContent(stream)));
        var client = new CwlLotteryClient(http, TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.FetchAsync(false, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(stream.ReadCanceled);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public async Task InconsistentPageMetadataRejectsBatch(int secondPageNo, int secondPageCount)
    {
        int request = 0;
        using var http = new HttpClient(new Handler(_ => new StringContent(++request == 1
            ? Page("2026112", "", "").Replace("\"pageNum\":1", "\"pageNum\":2")
            : Page("2026111", "", "").Replace("\"pageNo\":1", $"\"pageNo\":{secondPageNo}").Replace("\"pageNum\":1", $"\"pageNum\":{secondPageCount}"))));
        await Assert.ThrowsAsync<InvalidDataException>(() => new CwlLotteryClient(http).FetchAsync(true, null, CancellationToken.None));
    }

    [Fact]
    public async Task BodyExceedingLimitRejectsBatch()
    {
        using var http = new HttpClient(new Handler(_ => new StringContent(new string(' ', 4 * 1024 * 1024 + 1))));
        await Assert.ThrowsAsync<InvalidDataException>(() => new CwlLotteryClient(http).FetchAsync(false, null, CancellationToken.None));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpContent> content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) {Content = content(request)});
    }

    private sealed class StalledStream : Stream
    {
        public bool ReadCanceled { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            try { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
            catch (OperationCanceledException) { ReadCanceled = true; throw; }
        }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer,int offset,int count) => throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count) => throw new NotSupportedException();
    }
    [Fact]
    public void ParsesOfficialPayoutsAndFuyunSeparateField()
    {
        var page = CwlLotteryClient.ParsePage(Page("2026075", "5", ""));
        var draw = Assert.Single(page.Draws);
        Assert.Equal("2026075", draw.Issue);
        Assert.True(draw.SpecialPrizeEnabled);
        Assert.Equal(5m, draw.Payouts[7]);
        Assert.Equal(7840223m, draw.Payouts[1]);
    }

    [Fact]
    public void BlankSpecialFieldIsUnknownRatherThanLosingBet()
    {
        Assert.Null(Assert.Single(CwlLotteryClient.ParsePage(Page("2026112", "", "")).Draws).SpecialPrizeEnabled);
    }

    [Fact]
    public void OfficialFutureTransitionDoesNotChangeEarlierDraw()
    {
        var page = CwlLotteryClient.ParsePage(Page("2026075", "5", "根据规则，自2026076期（2026年07月05日开奖）起不执行特别规定。"));
        var later = CwlLotteryClient.ParsePage(Page("2026076", "", ""));
        var draws = CwlLotteryClient.ResolveSpecialRules([page, later]);
        Assert.True(draws.Single(x => x.Issue == "2026075").SpecialPrizeEnabled);
        Assert.False(draws.Single(x => x.Issue == "2026076").SpecialPrizeEnabled);
    }

    [Theory]
    [InlineData("<html>not json</html>")]
    [InlineData("{\"state\":1,\"result\":[]}")]
    [InlineData("{\"state\":0,\"result\":[]}")]
    public void InvalidResponsesDoNotBecomeEmptyHistory(string json) =>
        Assert.ThrowsAny<Exception>(() => CwlLotteryClient.ParsePage(json));

    [Fact]
    public void DuplicateRedBallRejectsEntirePage() =>
        Assert.ThrowsAny<Exception>(() => CwlLotteryClient.ParsePage(Page("2026112", "", "").Replace("08,12,18", "08,08,18")));

    private static string Page(string issue, string money, string notice) => $$"""
        {"state":0,"total":1,"pageNum":1,"pageNo":1,"result":[{
        "code":"{{issue}}","date":"2026-07-02(四)","red":"08,12,18,21,24,30","blue":"01",
        "fyjMoney":"{{money}}","specialRuleInfo":"{{notice}}",
        "prizegrades":[{"type":1,"typemoney":"7840223"},{"type":2,"typemoney":"556906"},
        {"type":3,"typemoney":"3000"},{"type":4,"typemoney":"200"},{"type":5,"typemoney":"10"},{"type":6,"typemoney":"5"}]}]}
        """;
}
