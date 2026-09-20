namespace ToolsBox.Core.WorkCountdown;

public sealed record WorkSchedule(
    DateTime Start,
    DateTime? NormalEnd,
    DateTime End,
    bool IsLate,
    TimeSpan ExcludedBreaks);
