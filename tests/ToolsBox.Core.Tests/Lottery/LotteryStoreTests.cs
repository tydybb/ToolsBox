using ToolsBox.Core.Lottery;
namespace ToolsBox.Core.Tests.Lottery;
public class LotteryStoreTests
{
    [Fact] public void OversizedSavePreservesPreviousReadableFile()
    {
        var path=Path.Combine(Path.GetTempPath(),"lottery-"+Guid.NewGuid()+".json");
        try
        {
            var store=new LotteryStore(path, 512);var state=new LotteryState();store.Save(state);
            var before=File.ReadAllText(path);
            state.Favorites.Add(new(new string('x',1024),new(new[]{1,2,3,4,5,6},1)));
            Assert.Throws<InvalidDataException>(()=>store.Save(state));
            Assert.Equal(before,File.ReadAllText(path));Assert.Empty(store.Load().Favorites);
        }finally{File.Delete(path);}
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Draws\":[],\"Purchases\":[]}")]
    [InlineData("{\"Draws\":[],\"Favorites\":[]}")]
    [InlineData("{\"Purchases\":[],\"Favorites\":[]}")]
    public void MissingRequiredListsAreCorrupt(string json)
    {
        var path=Path.Combine(Path.GetTempPath(),"lottery-"+Guid.NewGuid()+".json");
        try {File.WriteAllText(path,json);Assert.ThrowsAny<Exception>(()=>new LotteryStore(path).Load());}
        finally {File.Delete(path);}
    }
    [Fact] public void OversizedFileIsRejected()
    {
        var path=Path.Combine(Path.GetTempPath(),"lottery-"+Guid.NewGuid()+".json");
        try
        {
            using(var stream=File.Create(path))stream.SetLength(64L*1024*1024+1);
            Assert.Throws<InvalidDataException>(()=>new LotteryStore(path).Load());
        } finally {File.Delete(path);}
    }
    [Fact] public void RoundTripAndCorruptData()
    {
        var path=Path.Combine(Path.GetTempPath(),"lottery-"+Guid.NewGuid()+".json");
        try
        {
            var store=new LotteryStore(path);Assert.Empty(store.Load().Purchases);var state=new LotteryState();
            state.Purchases.Add(new(Guid.NewGuid(),"2026112",new(new[]{1,2,3,4,5,6},1),2,LotteryBuyMode.Fake,DateTimeOffset.UtcNow));
            state.Favorites.Add(new("我的号码",state.Purchases[0].Numbers));store.Save(state);var read=store.Load();
            Assert.Equal(state.Purchases[0].Id,read.Purchases[0].Id);Assert.Equal("我的号码",read.Favorites[0].Name);
            File.WriteAllText(path,"{broken");Assert.ThrowsAny<Exception>(()=>store.Load());
            File.WriteAllText(path,"{\"Purchases\":[{\"Issue\":\"bad\"}]}");Assert.ThrowsAny<Exception>(()=>store.Load());
        }finally{File.Delete(path);}
    }
    [Fact] public void InvalidSavePreservesPreviousFile()
    {
        var path=Path.Combine(Path.GetTempPath(),"lottery-"+Guid.NewGuid()+".json");
        try
        {
            var store=new LotteryStore(path);var state=new LotteryState();store.Save(state);
            var before=File.ReadAllText(path);
            state.Purchases.Add(new(Guid.NewGuid(),"2026112",new(new[]{1,2,3,4,5,6},1),0,LotteryBuyMode.Real,DateTimeOffset.UtcNow));
            Assert.ThrowsAny<ArgumentException>(()=>store.Save(state));Assert.Equal(before,File.ReadAllText(path));
            state.Purchases.Clear();var purchase=new LotteryPurchase(Guid.NewGuid(),"2026112",new(new[]{1,2,3,4,5,6},1),1,LotteryBuyMode.Real,DateTimeOffset.UtcNow);
            state.Purchases.AddRange(new[]{purchase,purchase});Assert.Throws<InvalidDataException>(()=>store.Save(state));
        }finally{File.Delete(path);}
    }
}
