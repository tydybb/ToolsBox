using System.Text.Json;

namespace ToolsBox.Core.Lottery;

public sealed class LotteryStore(string path, int maxBytes = 64 * 1024 * 1024)
{
    private readonly int _maxBytes = maxBytes > 0 ? maxBytes : throw new ArgumentOutOfRangeException(nameof(maxBytes));
    private static readonly JsonSerializerOptions Options = new() {WriteIndented=true};
    public LotteryState Load()
    {
        FileStream stream;
        try {stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);}
        catch (FileNotFoundException) {return new();}
        catch (DirectoryNotFoundException) {return new();}
        using var opened = stream;
        if (stream.Length > _maxBytes) throw new InvalidDataException("彩票数据超过存储大小限制。");
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = stream.Read(chunk)) != 0)
        {
            if (buffer.Length + read > _maxBytes) throw new InvalidDataException("彩票数据超过存储大小限制。");
            buffer.Write(chunk, 0, read);
        }
        var state=JsonSerializer.Deserialize<LotteryState>(buffer.GetBuffer().AsSpan(0, (int)buffer.Length),Options) ?? throw new InvalidDataException("彩票数据为空。");
        Validate(state);return state;
    }
    public void Save(LotteryState state)
    {
        Validate(state);
        var full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                JsonSerializer.Serialize(stream,state,Options);
                if (stream.Length > _maxBytes) throw new InvalidDataException("彩票数据超过存储大小限制，原有数据已保留。");
                stream.Flush(true);
            }
            File.Move(temp,full,true);
        }
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
    private static void Validate(LotteryState s)
    {
        if(s is null || s.Draws is null || s.Purchases is null || s.Favorites is null)throw new InvalidDataException("彩票数据列表缺失。");
        foreach(var d in s.Draws)LotteryRules.ValidateDraw(d);
        foreach(var p in s.Purchases)LotteryRules.ValidatePurchase(p);
        if(s.Draws.Select(x=>x.Issue).Distinct().Count()!=s.Draws.Count || s.Purchases.Select(x=>x.Id).Distinct().Count()!=s.Purchases.Count
            || s.Favorites.Any(x=>x is null || string.IsNullOrWhiteSpace(x.Name) || x.Numbers is null))throw new InvalidDataException("彩票数据包含重复或无效记录。");
    }
}
