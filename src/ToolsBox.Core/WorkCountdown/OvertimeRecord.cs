using System.Text.Json.Serialization;

namespace ToolsBox.Core.WorkCountdown;

public sealed record OvertimeRecord(
    [property: JsonRequired] DateOnly WorkDate,
    [property: JsonRequired] DateTime StartedAt,
    [property: JsonRequired] DateTime? NormalEnd,
    [property: JsonRequired] DateTime FinishedAt,
    [property: JsonRequired] long OvertimeTicks)
{
    [JsonIgnore]
    public TimeSpan Duration => TimeSpan.FromTicks(OvertimeTicks);

    public void Validate()
    {
        var overtimeStart = NormalEnd is { } end && end > StartedAt ? end : StartedAt;
        if (WorkDate != DateOnly.FromDateTime(StartedAt)
            || FinishedAt < StartedAt
            || NormalEnd is { } normalEnd && DateOnly.FromDateTime(normalEnd) != WorkDate
            || OvertimeTicks != CountdownCalculator.CalculateActualOvertime(overtimeStart, FinishedAt).Ticks)
            throw new InvalidDataException("加班历史记录中的日期、时间或净加班时长不一致。");
    }
}
