using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ToolsBox.Core.Attendance;

namespace ToolsBox.Core.Tests.Attendance;
public sealed class WalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommittedWalAppliesOnlyValidatedEncryptedFrames(bool corrupt)
    {
        string dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            byte[] page=new byte[4096]; Encoding.ASCII.GetBytes("SQLite format 3\0").CopyTo(page,0);
            using var aes=Aes.Create(); aes.Key=new byte[16];
            File.WriteAllBytes(Path.Combine(dir,"db"),aes.EncryptEcb(page,PaddingMode.None));
            page[100]=42;
            byte[] wal=BuildWal(aes.EncryptEcb(page,PaddingMode.None));
            if(corrupt) wal[99]^=1;
            File.WriteAllBytes(Path.Combine(dir,"db-wal"),wal);
            if(corrupt) Assert.Throws<InvalidDataException>(()=>DingTalkDbDecryptor.DecryptToMemory(new byte[16],Path.Combine(dir,"db"),null));
            else Assert.Equal(42,DingTalkDbDecryptor.DecryptToMemory(new byte[16],Path.Combine(dir,"db"),null)[100]);
        }
        finally {Directory.Delete(dir,true);}
    }

    [Fact]
    public void UncommittedWalDoesNotApply()
    {
        string dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            byte[] page=new byte[4096]; Encoding.ASCII.GetBytes("SQLite format 3\0").CopyTo(page,0);
            using var aes=Aes.Create();aes.Key=new byte[16];
            File.WriteAllBytes(Path.Combine(dir,"db"),aes.EncryptEcb(page,PaddingMode.None));
            page[100]=42;
            File.WriteAllBytes(Path.Combine(dir,"db-wal"),BuildWal(aes.EncryptEcb(page,PaddingMode.None),false));
            Assert.Equal(0,DingTalkDbDecryptor.DecryptToMemory(new byte[16],Path.Combine(dir,"db"),null)[100]);
        }
        finally {Directory.Delete(dir,true);}
    }

    internal static byte[] BuildWal(byte[] encrypted, bool committed=true)
    {
        byte[] wal=new byte[32+24+4096];
        void Write(int offset,uint value)=>BinaryPrimitives.WriteUInt32BigEndian(wal.AsSpan(offset),value);
        Write(0,0x377f0682); Write(4,3007000); Write(8,4096); Write(16,123); Write(20,456);
        var sum=Checksum(wal.AsSpan(0,24),0,0); Write(24,sum.Item1); Write(28,sum.Item2);
        Write(32,1); Write(36,committed?1U:0U); Write(40,123); Write(44,456); encrypted.CopyTo(wal,56);
        sum=Checksum(wal.AsSpan(32,8),sum.Item1,sum.Item2); sum=Checksum(encrypted,sum.Item1,sum.Item2);
        Write(48,sum.Item1); Write(52,sum.Item2); return wal;
    }
    private static (uint,uint) Checksum(ReadOnlySpan<byte> data,uint a,uint b)
    {
        unchecked {for(int i=0;i<data.Length;i+=8){a+=BinaryPrimitives.ReadUInt32LittleEndian(data[i..])+b; b+=BinaryPrimitives.ReadUInt32LittleEndian(data[(i+4)..])+a;}}
        return(a,b);
    }

    [Fact]
    public void UidCandidatesIncludesBoundedChildLogs()
    {
        string dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(dir,"child"));
        try
        {
            File.WriteAllText(Path.Combine(dir,"child","sample.log"),"real_uid=123456789");
            File.WriteAllText(Path.Combine(dir,"child","rotated.2026-09-23"),"userId: 987654321");
            var candidates=DingTalkKeyVault.UidCandidates(dir);
            Assert.Contains("123456789",candidates);
            Assert.Contains("987654321",candidates);
        }
        finally{Directory.Delete(dir,true);}
    }

    [Fact]
    public void UidCandidatesReadConfigFieldsWithoutLogDirectory()
    {
        string dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string config=Path.Combine(dir,"user_config");
            File.WriteAllText(config,"{\"salt\":\"s\",\"userId\":123456789,\"uid\":\"abc123\",\"real_uid\":\"1234\"}");
            var paths=new DingTalkAccountPaths(dir,Path.Combine(dir,"db"),null,config,null);
            var candidates=DingTalkKeyVault.UidCandidates(paths);
            Assert.Equal("123456789",candidates[0]);
            Assert.Contains("abc123",candidates);
            Assert.Contains("1234",candidates);
        }
        finally{Directory.Delete(dir,true);}
    }

    [Fact]
    public void UidCandidatesReadNestedAndUnnamedConfigDigits()
    {
        string dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string config=Path.Combine(dir,"user_config");
            File.WriteAllText(config,"{\"account\":{\"profile\":{\"memberId\":\"987654321\"}},\"loginTime\":1790034640777}");
            var paths=new DingTalkAccountPaths(dir,Path.Combine(dir,"db"),null,config,null);
            var candidates=DingTalkKeyVault.UidCandidates(paths);
            Assert.Contains("987654321",candidates);
            Assert.Contains("1790034640777",candidates);
        }
        finally{Directory.Delete(dir,true);}
    }

    [Fact]
    public void UidCandidatesIncludeAccountIdDirName()
    {
        string dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var paths=new DingTalkAccountPaths(Path.Combine(dir,"499135689012_v3"),Path.Combine(dir,"db"),null,Path.Combine(dir,"missing_config"),null);
            Assert.Contains("499135689012",DingTalkKeyVault.UidCandidates(paths));
        }
        finally{Directory.Delete(dir,true);}
    }

    [Fact]
    public void UidCandidatesReadBase64WrappedConfig()
    {
        string dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string config=Path.Combine(dir,"user_config");
            File.WriteAllText(config,Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"realUid\":\"987654321\"}")));
            var paths=new DingTalkAccountPaths(dir,Path.Combine(dir,"db"),null,config,null);
            Assert.Contains("987654321",DingTalkKeyVault.UidCandidates(paths));
        }
        finally{Directory.Delete(dir,true);}
    }
}
