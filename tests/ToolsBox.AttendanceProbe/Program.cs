using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ToolsBox.Core.Attendance;
using Microsoft.Data.Sqlite;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

// Deliberately emits only format diagnostics. Never print paths, account identifiers, keys or content.
try
{
    var located = new DingTalkDataLocator().Locate();
    if (located.Paths is not { } paths) { Console.WriteLine("{\"Located\":false}"); return; }
    string? salt = DingTalkKeyVault.ReadSalt(paths.UserConfigPath);
    if (salt is null) { Console.WriteLine("{\"ConfigSupported\":false}"); return; }
    var candidates = DingTalkKeyVault.UidCandidates(paths.LogDir).ToList();
    if(paths.LogDir is not null && Directory.Exists(paths.LogDir))
        foreach(var child in Directory.EnumerateDirectories(paths.LogDir).Take(8))
            if((File.GetAttributes(child) & FileAttributes.ReparsePoint)==0) candidates.AddRange(DingTalkKeyVault.UidCandidates(child));
    byte[]? key = DingTalkKeyVault.FindVerifiedKey(candidates, salt, DingTalkKeyVault.ReadSharedHead(paths.DbPath, 16));
    if (key is null)
    {
        bool logExists = paths.LogDir is not null && Directory.Exists(paths.LogDir);
        Console.WriteLine(JsonSerializer.Serialize(new { KeyVerified=false, CandidateCount=candidates.Count, LogDirectoryExists=logExists, LogFiles=logExists ? Directory.EnumerateFiles(paths.LogDir!).Count() : 0, LogSubdirectories=logExists ? Directory.EnumerateDirectories(paths.LogDir!).Count() : 0 }));
        return;
    }
    try
    {
        byte[] wal = paths.WalPath is null ? Array.Empty<byte>() : DingTalkKeyVault.ReadSharedHead(paths.WalPath, 16 * 1024 * 1024);
        if (wal.Length < 32) { Console.WriteLine("{\"WalPresent\":false}"); return; }
        uint magic = BinaryPrimitives.ReadUInt32BigEndian(wal);
        bool little = magic == 0x377f0682;
        uint page = BinaryPrimitives.ReadUInt32BigEndian(wal.AsSpan(8));
        if (page != 4096) { Console.WriteLine(JsonSerializer.Serialize(new { PageSize=page })); return; }
        (uint a, uint b) = Inspector.Checksum(wal.AsSpan(0, 24), 0, 0, little);
        bool header = a == BinaryPrimitives.ReadUInt32BigEndian(wal.AsSpan(24)) && b == BinaryPrimitives.ReadUInt32BigEndian(wal.AsSpan(28));
        int frames = (wal.Length - 32) / 4120, cipherMatches = 0, plainMatches = 0, saltMatches = 0, commits = 0;
        for (int i = 0; i < frames; i++)
        {
            int offset = 32 + i * 4120;
            if (wal.AsSpan(offset+8, 8).SequenceEqual(wal.AsSpan(16, 8))) saltMatches++;
            if (BinaryPrimitives.ReadUInt32BigEndian(wal.AsSpan(offset+4)) != 0) commits++;
            var first = Inspector.Checksum(wal.AsSpan(offset, 8), a, b, little);
            var encrypted = Inspector.Checksum(wal.AsSpan(offset+24, 4096), first.Item1, first.Item2, little);
            byte[] plain = DingTalkDbDecryptor.DecryptPages(key, wal.AsSpan(offset+24,4096));
            var decrypted = Inspector.Checksum(plain, first.Item1, first.Item2, little);
            CryptographicOperations.ZeroMemory(plain);
            a = BinaryPrimitives.ReadUInt32BigEndian(wal.AsSpan(offset+16)); b = BinaryPrimitives.ReadUInt32BigEndian(wal.AsSpan(offset+20));
            if (encrypted.Item1 == a && encrypted.Item2 == b) cipherMatches++;
            if (decrypted.Item1 == a && decrypted.Item2 == b) plainMatches++;
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Date=DateTime.Now.ToString("yyyy-MM-dd"), WalBytes=wal.Length, PageSize=page, HeaderChecksumValid=header, Frames=frames, TrailingBytes=(wal.Length-32)%4120, SaltMatchingFrames=saltMatches, CommittedFrames=commits, CipherChecksumMatchingFrames=cipherMatches, PlainChecksumMatchingFrames=plainMatches }));
        byte[] memory = DingTalkDbDecryptor.DecryptToMemory(key, paths.DbPath, paths.WalPath);
        try { Inspector.Inspect(memory); }
        finally { CryptographicOperations.ZeroMemory(memory); }
        var status = new DingTalkAttendanceChecker().Check(DateTime.Now);
        Console.WriteLine(JsonSerializer.Serialize(new {status.Obtained,status.ClockedIn,status.ClockInTime,status.TodayMessages}));
    }
    finally { CryptographicOperations.ZeroMemory(key); }
}

catch { Console.WriteLine("{\"ProbeFailed\":true}"); }

static class Inspector
{
    [DllImport("e_sqlite3", EntryPoint="sqlite3_deserialize", CallingConvention=CallingConvention.Cdecl)]
    private static extern int Deserialize(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string schema, IntPtr buffer,long length,long capacity,uint flags);
    public static void Inspect(byte[] memory)
    {
        memory[18]=memory[19]=1;
        var handle=GCHandle.Alloc(memory,GCHandleType.Pinned);
        try
        {
            using var db=new SqliteConnection("Data Source=:memory:;Pooling=False");db.Open();
            if(Deserialize(db.Handle!.DangerousGetHandle(),"main",handle.AddrOfPinnedObject(),memory.Length,memory.Length,4)!=0)throw new InvalidDataException();
            using var cmd=db.CreateCommand();cmd.CommandText="PRAGMA query_only=ON; PRAGMA temp_store=MEMORY; PRAGMA quick_check(1)";
            Console.WriteLine(JsonSerializer.Serialize(new {QuickCheckValid=(cmd.ExecuteScalar() as string)=="ok"}));
            cmd.CommandText="SELECT name FROM sqlite_master WHERE type='table' AND name GLOB 'tbmsg*' LIMIT 1025";
            var names=new List<string>();using(var r=cmd.ExecuteReader()){while(r.Read())names.Add(r.GetString(0));}
            var columns=new HashSet<string>();var counts=new Dictionary<long,int>(); var keys=new HashSet<string>();
            int cards=0, withClockIn=0,withSuccess=0,withLate=0,withDate=0,withTime=0, matched=0;
            foreach(var name in names)
            {
                cmd.CommandText=$"PRAGMA table_info(\"{name.Replace("\"","\"\"")}\")";
                using(var r=cmd.ExecuteReader()){while(r.Read())columns.Add(r.GetString(1));}
                cmd.CommandText=$"SELECT contentType,content,createdAt FROM \"{name.Replace("\"","\"\"")}\" WHERE (createdAt BETWEEN $lo AND $hi) OR (createdAt BETWEEN $lm AND $hm) LIMIT 10001";
                cmd.Parameters.Clear();long lo=new DateTimeOffset(DateTime.Today).ToUnixTimeSeconds(),hi=DateTimeOffset.Now.ToUnixTimeSeconds();
                cmd.Parameters.AddWithValue("$lo",lo);cmd.Parameters.AddWithValue("$hi",hi);cmd.Parameters.AddWithValue("$lm",lo*1000);cmd.Parameters.AddWithValue("$hm",hi*1000);
                using var reader=cmd.ExecuteReader();
                while(reader.Read())
                {
                    long type=reader.GetInt64(0);counts[type]=counts.GetValueOrDefault(type)+1;
                    if(type!=2950 || reader.IsDBNull(1))continue;
                    string text=reader.GetString(1);if(text.Length>65536)continue;
                    using var doc=JsonDocument.Parse(text);if(doc.RootElement.ValueKind!=JsonValueKind.Object)continue;
                    foreach(var p in doc.RootElement.EnumerateObject())keys.Add(p.Name);
                    if(!doc.RootElement.TryGetProperty("attachments",out var attachments)||attachments.ValueKind!=JsonValueKind.Array)continue;
                    foreach(var attachment in attachments.EnumerateArray())
                    {
                        if(!attachment.TryGetProperty("extension",out var extension)||extension.ValueKind!=JsonValueKind.String)continue;
                        using var card=JsonDocument.Parse(extension.GetString()!);
                        foreach(var p in card.RootElement.EnumerateObject())keys.Add("extension."+p.Name);
                        if(!card.RootElement.TryGetProperty("interactiveCardLastMessage",out var message)||message.ValueKind!=JsonValueKind.String)continue;
                        string value=message.GetString()!; cards++;
                        if(value.Contains("上班打卡"))
                        {
                            bool hasCreate=card.RootElement.TryGetProperty("messageCreateTime",out var created);
                            string rawCreate=hasCreate?created.ToString():"";
                            bool numeric=long.TryParse(rawCreate,out var createStamp);
                            bool today=numeric && (createStamp>100000000000?DateTimeOffset.FromUnixTimeMilliseconds(createStamp):DateTimeOffset.FromUnixTimeSeconds(createStamp)).LocalDateTime.Date==DateTime.Today;
                            Console.WriteLine(JsonSerializer.Serialize(new {ClockInCard=true,CreateTimeType=hasCreate?created.ValueKind.ToString():"Missing",CreateTimeNumeric=numeric,CreateTimeToday=today,TimeFirstExact=Regex.IsMatch(value,@"\A\d{2}:\d{2}\s+上班打卡\s*[·：:]?\s*(成功|迟到)\z"),ClockFirstExact=Regex.IsMatch(value,@"\A上班打卡\s*[·：:]?\s*(成功|迟到)\s+\d{2}:\d{2}\z"),EmbeddedContentType=doc.RootElement.TryGetProperty("contentType",out var ct)&&ct.ToString()=="2950"}));
                        }
                        if(value.Contains("上班打卡"))withClockIn++;if(value.Contains("成功"))withSuccess++;if(value.Contains("迟到"))withLate++;
                        if(Regex.IsMatch(value,@"\d{4}[-/]\d{1,2}[-/]\d{1,2}"))withDate++;if(Regex.IsMatch(value,@"\d{1,2}:\d{2}"))withTime++;
                    }
                    long stamp=reader.GetInt64(2);var received=stamp>100000000000?DateTimeOffset.FromUnixTimeMilliseconds(stamp).LocalDateTime:DateTimeOffset.FromUnixTimeSeconds(stamp).LocalDateTime;
                    matched+=AttendanceMessageParser.ParseAll(new[]{((string?)text,received)}).Count;
                }
            }
            Console.WriteLine(JsonSerializer.Serialize(new {TableCount=names.Count,Fields=columns.Order().ToArray(),Types=counts,CardFields=keys.Order().ToArray(),Cards=cards,WithClockIn=withClockIn,WithSuccess=withSuccess,WithLate=withLate,WithExplicitDate=withDate,WithTime=withTime,Matched=matched}));
        }
        finally{handle.Free();}
    }
public static (uint, uint) Checksum(ReadOnlySpan<byte> bytes, uint a, uint b, bool little)
{
    unchecked
    {
        for (int i=0; i<bytes.Length; i+=8)
        {
            a += (little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[i..]) : BinaryPrimitives.ReadUInt32BigEndian(bytes[i..])) + b;
            b += (little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[(i+4)..]) : BinaryPrimitives.ReadUInt32BigEndian(bytes[(i+4)..])) + a;
        }
    }
    return (a,b);
}
}
