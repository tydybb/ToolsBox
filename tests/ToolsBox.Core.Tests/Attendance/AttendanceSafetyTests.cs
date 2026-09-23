using ToolsBox.Core.Attendance;
using System.Text.Json;

namespace ToolsBox.Core.Tests.Attendance;

public sealed class AttendanceSafetyTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 9, 0, 0);
    private static string Card(string text) => JsonSerializer.Serialize(new { attachments = new[] { new { extension = JsonSerializer.Serialize(new { interactiveCardLastMessage = text }) } } });

    [Theory]
    [InlineData("2026-09-22 08:30 上班打卡 成功", true)]
    [InlineData("2026-09-22 08:30 上班打卡 迟到", true)]
    [InlineData("2026-09-22 08:30 下班打卡 成功", false)]
    [InlineData("2026-09-22 08:30 打卡 成功", false)]
    [InlineData("2026-09-21 08:30 上班打卡 成功", false)]
    [InlineData("2026-09-22 10:30 上班打卡 成功", false)]
    [InlineData("2026-09-22 28:30 上班打卡 成功", false)]
    [InlineData("08:30 上班打卡 成功", false)]
    public void OnlyExplicitDatedCardCanSuppress(string text, bool expected)
    {
        var records = AttendanceMessageParser.ParseAll(new[] { ((string?)Card(text), Now) });
        Assert.Equal(expected, AttendanceStatus.FromRecords(1, records, Now).ClockedIn);
    }

    [Fact]
    public void PlainChatDoesNotSuppress() => Assert.Empty(AttendanceMessageParser.ParseAll(new[] { ((string?)"2026-09-22 08:30 上班打卡 成功", Now) }));

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    [InlineData(1, false)]
    public void UndatedCardRequiresOwnCurrentDayCreationTimestamp(int dayOffset, bool expected)
    {
        string card = JsonSerializer.Serialize(new { contentType=2950, attachments=new[] { new { extension=JsonSerializer.Serialize(new { interactiveCardLastMessage="08:30 上班打卡·成功", messageCreateTime=new DateTimeOffset(Now.AddDays(dayOffset)).ToUnixTimeMilliseconds().ToString() }) } } });
        Assert.Equal(expected,AttendanceStatus.FromRecords(1,AttendanceMessageParser.ParseAll(new[]{((string?)card,Now)}),Now).ClockedIn);
    }

    [Fact]
    public void OldRecordCannotSuppress() => Assert.False(AttendanceStatus.FromRecords(1, new[] { new AttendanceRecord("08:30", "上班打卡", "成功", Now.AddDays(-1)) }, Now).ClockedIn);

    [Fact]
    public void AmbiguousAccountsRequireSelection()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            foreach (string name in new[] { "a_v3", "b_v3" }) { Directory.CreateDirectory(Path.Combine(dir, name, "DBFiles")); File.WriteAllBytes(Path.Combine(dir, name, "DBFiles", "dingtalk.db"), new byte[16]); }
            var located=new DingTalkDataLocator(new[] { dir }).Locate();
            Assert.Null(located.Paths);
            Assert.Equal(2,located.Candidates.Count);
            Assert.NotNull(new DingTalkDataLocator(new[] { Path.Combine(dir, "a_v3") }).Locate().Paths);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void ExpiredExplicitRootReportsSelectionGuidance()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Contains("已不存在", new DingTalkDataLocator(new[] { Path.Combine(root, "gone_v3") }).Locate().Error);
            Directory.CreateDirectory(root);
            Assert.Contains("不是钉钉账号数据目录", new DingTalkDataLocator(new[] { root }).Locate().Error);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void MissingDirectoryIsUnknown() => Assert.False(new DingTalkAttendanceChecker(new DingTalkDataLocator(new[] { Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) })).Check(Now).Obtained);

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"other\":\"0123456789abcdef\"}")]
    public void UnsupportedConfigurationIsUnknown(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try { File.WriteAllText(path, content); Assert.Null(DingTalkKeyVault.ReadSalt(path)); }
        finally { File.Delete(path); }
    }
}
