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
        try {File.WriteAllText(Path.Combine(dir,"child","sample.log"),"real_uid=123456789"); Assert.Contains("123456789",DingTalkKeyVault.UidCandidates(dir));}
        finally{Directory.Delete(dir,true);}
    }
}
