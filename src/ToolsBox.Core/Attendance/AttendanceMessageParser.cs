using System.Text.Json;
using System.Text.RegularExpressions;

namespace ToolsBox.Core.Attendance;

/// <summary>本地卡片线索；不等同于官方考勤记录。</summary>
public sealed record AttendanceRecord(string Time, string Kind, string Result, DateTime ReceivedAt)
{
    /// <summary>仅显式上班打卡成功或迟到可供后续日期校验。</summary>
    public bool IsClockInSuccess =>
        Kind == "上班打卡" && Result is "成功" or "迟到";
}

public static class AttendanceMessageParser
{
    // Local card evidence only; this is not an authoritative server attendance API.
    public static IReadOnlyList<AttendanceRecord> ParseAll(IEnumerable<(string? Content, DateTime ReceivedAt)> messages)
    {
        var records = new List<AttendanceRecord>();
        foreach (var message in messages.Take(10001))
        {
            if (message.Content is not { Length: > 0 and <= 65536 }) continue;
            try
            {
                using var document = JsonDocument.Parse(message.Content, new JsonDocumentOptions { MaxDepth = 12 });
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("attachments", out var attachments) || attachments.ValueKind != JsonValueKind.Array) continue;
                foreach (var attachment in attachments.EnumerateArray().Take(16))
                {
                    if (attachment.ValueKind != JsonValueKind.Object || !attachment.TryGetProperty("extension", out var extension) || extension.ValueKind != JsonValueKind.String) continue;
                    using var card = JsonDocument.Parse(extension.GetString()!, new JsonDocumentOptions { MaxDepth = 8 });
                    if (card.RootElement.ValueKind != JsonValueKind.Object || !card.RootElement.TryGetProperty("interactiveCardLastMessage", out var last) || last.ValueKind != JsonValueKind.String) continue;
                    string text = last.GetString()!;
                    if (card.RootElement.TryGetProperty("messageCreateTime", out var created))
                    {
                        if (!long.TryParse(created.ToString(), out long stamp)) continue;
                        DateTime cardCreated;
                        try { cardCreated = (stamp > 100_000_000_000L ? DateTimeOffset.FromUnixTimeMilliseconds(stamp) : DateTimeOffset.FromUnixTimeSeconds(stamp)).LocalDateTime; }
                        catch (ArgumentOutOfRangeException) { continue; }
                        if (cardCreated.Date != message.ReceivedAt.Date || cardCreated > message.ReceivedAt) continue;
                        // The actual punch time stays in the card text. The card timestamp supplies only its date.
                        if (Regex.IsMatch(text, @"\A\d{2}:\d{2}\s+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)))
                        {
                            if (!document.RootElement.TryGetProperty("contentType", out var type) || type.ToString() != "2950") continue;
                            text = cardCreated.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + " " + text;
                        }
                    }
                    // Deliberately exact: embedded quotes, extra prose, generic punches and unbound dates are ambiguous.
                    var match = Regex.Match(text, @"\A(?<date>\d{4}-\d{2}-\d{2})\s+(?<time>\d{2}:\d{2})\s+上班打卡\s*[·：:]?\s*(?<result>成功|迟到)\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
                    if (!match.Success || !DateTime.TryParseExact(match.Groups["date"].Value + " " + match.Groups["time"].Value, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var actual)) continue;
                    if (actual.Date != message.ReceivedAt.Date || actual > message.ReceivedAt) continue;
                    records.Add(new AttendanceRecord(match.Groups["time"].Value, "上班打卡", match.Groups["result"].Value, message.ReceivedAt));
                }
            }
            catch (JsonException) { }
            catch (RegexMatchTimeoutException) { }
        }
        return records.OrderBy(record => record.ReceivedAt).ToArray();
    }
}
