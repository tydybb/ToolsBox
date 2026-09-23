using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text.Json;
using ToolsBox.Core.Attendance;

namespace ToolsBox.Core.Tests.Attendance;
public sealed class LocalReaderTests
{
    [Theory]
    [InlineData(2950, true)]
    [InlineData(1, false)]
    public void SyntheticEncryptedDatabaseReadStaysInMemory(int contentType, bool expected)
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "a_v3", "DBFiles"));
        Directory.CreateDirectory(Path.Combine(dir, "log"));
        string source = Path.Combine(dir, "synthetic.db");
        var now = new DateTime(2026, 9, 22, 9, 0, 0);
        try
        {
            using (var connection = new SqliteConnection($"Data Source={source};Pooling=False"))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "PRAGMA page_size=4096; CREATE TABLE tbmsg(content TEXT, createdAt INTEGER, contentType INTEGER); INSERT INTO tbmsg VALUES ($content, $stamp, $type)";
                cmd.Parameters.AddWithValue("$content", JsonSerializer.Serialize(new { attachments = new[] { new { extension = JsonSerializer.Serialize(new { interactiveCardLastMessage = "2026-09-22 08:30 上班打卡 成功" }) } } }));
                cmd.Parameters.AddWithValue("$stamp", new DateTimeOffset(now).ToUnixTimeMilliseconds());
                cmd.Parameters.AddWithValue("$type", contentType);
                cmd.ExecuteNonQuery();
                for(int i=0;i<127;i++) {cmd.CommandText=$"CREATE TABLE tbmsg{i}(content TEXT, createdAt INTEGER, contentType INTEGER)";cmd.ExecuteNonQuery();}
            }
            File.WriteAllText(Path.Combine(dir, "a_v3", "user_config"), "{\"salt\":\"synthetic\"}");
            File.WriteAllText(Path.Combine(dir, "log", "sample.log"), "real_uid=123456789");
            using var aes = Aes.Create(); aes.Key = DingTalkKeyVault.DeriveKey("123456789", "synthetic");
            File.WriteAllBytes(Path.Combine(dir, "a_v3", "DBFiles", "dingtalk.db"), aes.EncryptEcb(File.ReadAllBytes(source), PaddingMode.None));
            string sentinel = Path.Combine(dir, "attendance-keep.db"); File.WriteAllText(sentinel, "keep");
            var result = new DingTalkAttendanceChecker(new DingTalkDataLocator(new[] { dir }), dir).Check(now);
            Assert.True(result.Obtained, result.Error);
            Assert.Equal(expected, result.ClockedIn);
            Assert.Equal("keep", File.ReadAllText(sentinel));
            Assert.Equal(2, Directory.GetFiles(dir).Length);
        }
        finally { Directory.Delete(dir, true); }
    }
}
