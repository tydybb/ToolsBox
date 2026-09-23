namespace ToolsBox.Core.Attendance;

/// <summary>一次钉钉打卡检测的结果。未获取（Obtained=false）与已获取但未打卡都视为“未获取到打卡状态”。</summary>
public sealed record AttendanceStatus(
    bool Obtained,
    bool ClockedIn,
    string? ClockInTime,
    string? LatestRecord,
    int TodayMessages,
    string? Error)
{
    public static AttendanceStatus Failure(string error) => new(false, false, null, null, 0, error);

    public static AttendanceStatus FromRecords(int todayMessages, IReadOnlyList<AttendanceRecord> records, DateTime now)
    {
        var today = records.Where(record => record.ReceivedAt.Date == now.Date && record.ReceivedAt <= now &&
            TimeSpan.TryParseExact(record.Time, @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture, out var time) &&
            time >= TimeSpan.Zero && time < TimeSpan.FromDays(1) && now.Date.Add(time) <= now).OrderBy(record => record.ReceivedAt).ToArray();
        AttendanceRecord? clockIn = today.LastOrDefault(record => record.IsClockInSuccess);
        AttendanceRecord? latest = today.LastOrDefault();
        return new AttendanceStatus(
            true,
            clockIn is not null,
            clockIn?.Time,
            latest is null ? null : $"{latest.Time} {latest.Kind}·{latest.Result}",
            todayMessages,
            null);
    }
}
