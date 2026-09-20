namespace ToolsBox.Core.WorkCountdown;

public enum DayKind
{
    Workday,
    RestDay,
    Unknown
}

public sealed record DayClassification(DayKind Kind, string Label);
