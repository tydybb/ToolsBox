using ToolsBox.Core.Lottery;
namespace ToolsBox.Core.Tests.Lottery;
public class LotteryRulesTests
{
    static LotteryNumbers Numbers => new(new[]{1,2,3,4,5,6},1);
    static LotteryDraw Draw(bool? special = true) => new("2026112", new(2026,9,27), Numbers, Enumerable.Range(1,7).ToDictionary(x=>x,x=>100m*x),special);
    static LotteryPurchase Buy(LotteryNumbers numbers,LotteryBuyMode mode=LotteryBuyMode.Real,int multiple=1) => new(Guid.NewGuid(),"2026112",numbers,multiple,mode,DateTimeOffset.UtcNow);
    [Theory]
    [InlineData(6,true,1)] [InlineData(6,false,2)] [InlineData(5,true,3)] [InlineData(5,false,4)]
    [InlineData(4,true,4)] [InlineData(4,false,5)] [InlineData(3,true,5)] [InlineData(2,true,6)]
    [InlineData(1,true,6)] [InlineData(0,true,6)] [InlineData(3,false,7)] [InlineData(2,false,0)]
    public void HighestTier(int reds,bool blue,int tier)
    {
        var n=new LotteryNumbers(Enumerable.Range(1,reds).Concat(Enumerable.Range(20,6-reds)).ToArray(),blue?1:2);
        var s=LotteryRules.Settle(Buy(n),Draw()); Assert.Equal(tier==0?null:(int?)tier,s.Tier); Assert.Equal(tier*100m,s.Prize);
    }
    [Fact] public void FinanceAndPending()
    {
        var r=Buy(Numbers,multiple:3); var f=Buy(Numbers,LotteryBuyMode.Fake,3);
        Assert.Equal(294m,LotteryRules.Settle(r,Draw()).Net); Assert.Equal(-294m,LotteryRules.Settle(f,Draw()).Net);
        Assert.Null(LotteryRules.Settle(f,null).Net);
        var state=new LotteryState();state.Purchases.AddRange(new[]{r,f}); var s=LotteryRules.Summarize(state);
        Assert.Equal(6m,s.RealOutlay); Assert.Equal(0m,s.FakeSettledNet);Assert.Equal(2,s.PendingCount);
        var three=Buy(new(new[]{1,2,3,20,21,22},2));Assert.True(LotteryRules.Settle(three,Draw(null)).IsPending);
        Assert.Equal(0m,LotteryRules.Settle(three,Draw(false)).Prize);
        Assert.True(LotteryRules.Settle(r,Draw() with {Payouts=new Dictionary<int,decimal>()}).IsPending);
    }
    [Fact] public void ValidationParsingRandomAndCost()
    {
        var a=new[]{6,5,4,3,2,1};var n=new LotteryNumbers(a,16);a[0]=33;Assert.Equal(new[]{1,2,3,4,5,6},n.Reds);
        Assert.Throws<ArgumentException>(()=>new LotteryNumbers(new[]{1,1,2,3,4,5},1));
        Assert.Throws<ArgumentException>(()=>new LotteryNumbers(new[]{0,2,3,4,5,6},1));
        Assert.Throws<ArgumentException>(()=>new LotteryNumbers(new[]{1,2,3,4,5,34},1));
        Assert.Throws<ArgumentException>(()=>new LotteryNumbers(new[]{1,2,3,4,5,6},17));
        Assert.Equal("01 02 03 04 05 06 + 16",LotteryNumbers.Parse("06,05,04,03,02,01 + 16").ToString());
        Assert.Equal(100,LotteryNumbers.MachinePick(100).Count);
        Assert.Throws<ArgumentOutOfRangeException>(()=>LotteryRules.BatchCost(102,99));
        Assert.Throws<ArgumentOutOfRangeException>(()=>LotteryRules.BatchCost(1,100));
    }
    [Fact] public void GuardCutoff()
    {
        var now=new DateTimeOffset(2026,9,29,19,59,59,TimeSpan.FromHours(8));
        Assert.Null(LotteryRules.BuyGuard("2026113",new(2026,9,29),Draw(),Array.Empty<LotteryDraw>(),now));
        Assert.NotNull(LotteryRules.BuyGuard("2026113",new(2026,9,29),Draw(),Array.Empty<LotteryDraw>(),now.AddSeconds(1)));
        Assert.NotNull(LotteryRules.BuyGuard("2026114",new(2026,9,29),Draw(),Array.Empty<LotteryDraw>(),now));
        Assert.NotNull(LotteryRules.BuyGuard("2026113",new(2026,10,1),Draw(),Array.Empty<LotteryDraw>(),now));
    }
    [Fact] public void RepeatedBetsAreIndependentAndZeroPayoutWaits()
    {
        var p=Buy(Numbers);var state=new LotteryState();state.Draws.Add(Draw());
        state.Purchases.AddRange(new[]{p,Buy(Numbers)});
        Assert.Equal(4m,LotteryRules.Summarize(state).RealOutlay);
        Assert.Equal(196m,LotteryRules.Summarize(state).RealSettledNet);
        Assert.True(LotteryRules.Settle(p,Draw() with {Payouts=new Dictionary<int,decimal>{{1,0m}}}).IsPending);
        Assert.Equal(20000m,LotteryRules.BatchCost(10000,1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>LotteryNumbers.MachinePick(0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>LotteryRules.BatchCost(1,0));
    }
    [Fact] public void ParsingIsCultureIndependent()
    {
        var previous=System.Globalization.CultureInfo.CurrentCulture;
        try {System.Globalization.CultureInfo.CurrentCulture=new("ar-SA");Assert.Equal("01 02 03 04 05 06 + 07",LotteryNumbers.Parse("01 02 03 04 05 06 + 07").ToString());}
        finally {System.Globalization.CultureInfo.CurrentCulture=previous;}
    }
    [Fact] public void GuardRejectsKnownStaleAndYearRollover()
    {
        var now=new DateTimeOffset(2026,9,29,12,0,0,TimeSpan.FromHours(8));
        Assert.NotNull(LotteryRules.BuyGuard("2026113",new(2026,9,29),Draw(),new[]{Draw() with {Issue="2026113"}},now));
        Assert.NotNull(LotteryRules.BuyGuard("2026113",new(2026,9,29),Draw(),Array.Empty<LotteryDraw>(),now.AddDays(10)));
        Assert.NotNull(LotteryRules.BuyGuard("2027001",new(2027,1,3),Draw() with {Issue="2026150",DrawDate=new(2026,12,31)},Array.Empty<LotteryDraw>(),new(2027,1,1,12,0,0,TimeSpan.FromHours(8))));
    }
}
