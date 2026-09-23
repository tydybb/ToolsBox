namespace ToolsBox.App.Attendance;

/// <summary>Pure clock policy; never wakes the computer and never emits catch-up alerts after 09:25.</summary>
public sealed class AttendancePolicy
{
    public DateTime? LastCheck { get; private set; }
    public DateTime? LastReminder { get; set; }
    public DateOnly? FinalReminderDate { get; set; }
    private DateOnly? _finalCheckDate;

    public bool TryBeginCheck(DateTime now, bool enabled, bool workday, bool unlocked, bool force)
    {
        if (!enabled || !workday || !unlocked || now.TimeOfDay < new TimeSpan(7,30,0) || now.TimeOfDay > new TimeSpan(9,30,0)) return false;
        var date = DateOnly.FromDateTime(now);
        bool final = now.Hour == 9 && now.Minute == 25 && _finalCheckDate != date;
        if (!force && !final && LastCheck is DateTime last && last.Date == now.Date && now - last < TimeSpan.FromMinutes(5)) return false;
        LastCheck = now;
        if (final) _finalCheckDate = date;
        return true;
    }

    public bool TryRemind(DateTime now, DateOnly? confirmed = null)
    {
        var date = DateOnly.FromDateTime(now);
        if (confirmed == date || now.TimeOfDay < new TimeSpan(7,30,0) || now.TimeOfDay >= new TimeSpan(9,26,0) || FinalReminderDate == date) return false;
        bool final = now.Hour == 9 && now.Minute == 25;
        if (!final && LastReminder is DateTime last && last.Date == now.Date && now - last < TimeSpan.FromMinutes(5)) return false;
        LastReminder = now;
        if (final) FinalReminderDate = date;
        return true;
    }
}
