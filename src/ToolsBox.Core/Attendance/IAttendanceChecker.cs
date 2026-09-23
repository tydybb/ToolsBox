namespace ToolsBox.Core.Attendance;

/// <summary>打卡状态检测器：定位钉钉数据、解密消息库、查询今日打卡消息。</summary>
public interface IAttendanceChecker
{
    /// <summary>执行一次检测；实现不得抛出异常，失败以 Obtained=false + Error 报告。</summary>
    AttendanceStatus Check(DateTime now);
}
