using ToolsBox.Core.Attendance;
using System.Security.Cryptography;
using System.Text;

namespace ToolsBox.Core.Tests.Attendance;
public sealed class DecryptSafetyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsPartialDatabaseAndNonemptyWal(bool wal)
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "source"), new byte[wal ? 4096 : 17]);
            File.WriteAllBytes(Path.Combine(dir, "wal"), new byte[32]);
            Assert.Throws<InvalidDataException>(() => DingTalkDbDecryptor.DecryptToFile(new byte[16], Path.Combine(dir, "source"), wal ? Path.Combine(dir, "wal") : null, Path.Combine(dir, "out")));
            Assert.False(File.Exists(Path.Combine(dir, "out")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NeverOverwritesDestination()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            byte[] plain = new byte[4096]; Encoding.ASCII.GetBytes("SQLite format 3\0").CopyTo(plain, 0);
            using var aes = Aes.Create(); aes.Key = new byte[16];
            File.WriteAllBytes(Path.Combine(dir, "source"), aes.EncryptEcb(plain, PaddingMode.None));
            File.WriteAllText(Path.Combine(dir, "out"), "keep");
            Assert.Throws<IOException>(() => DingTalkDbDecryptor.DecryptToFile(new byte[16], Path.Combine(dir, "source"), null, Path.Combine(dir, "out")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(dir, "out")));
        }
        finally { Directory.Delete(dir, true); }
    }
}
