using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.Core.Tests.WorkCountdown;

public sealed class HolidayCelebrationTests
{
    [Theory]
    // 节前最后一个工作日 / 假期内，且假期 ≥7 天 → 完整版（纸屑 + 烟花）
    [InlineData(2026, 9, 30, CelebrationLevel.Full)]   // 国庆 7 天前的最后一个工作日
    [InlineData(2026, 10, 1, CelebrationLevel.Full)]   // 国庆第一天
    [InlineData(2026, 10, 7, CelebrationLevel.Full)]   // 国庆最后一天
    [InlineData(2026, 2, 14, CelebrationLevel.Full)]   // 调休补班日，次日春节 9 天（那天就是节前最后一个工作日）
    // 假期 <7 天 → 轻量版（只撒纸屑）
    [InlineData(2026, 9, 24, CelebrationLevel.Light)]  // 中秋 3 天前的最后一个工作日
    [InlineData(2026, 9, 26, CelebrationLevel.Light)]  // 中秋假期内
    [InlineData(2026, 5, 2, CelebrationLevel.Light)]   // 劳动节 5 天，不足 7 天
    // 非假期相关日子 → 不放
    [InlineData(2026, 9, 29, CelebrationLevel.None)]   // 节前第 2 个工作日，还没到点
    [InlineData(2026, 9, 19, CelebrationLevel.None)]   // 周六，次日只是补班日不是假期
    [InlineData(2026, 9, 20, CelebrationLevel.None)]   // 补班日，次日不是假期
    [InlineData(2026, 11, 11, CelebrationLevel.None)]  // 普通工作日
    [InlineData(2026, 12, 31, CelebrationLevel.None)]  // 元旦在下一年，本机无 2027 年日历
    [InlineData(2027, 9, 30, CelebrationLevel.None)]   // 无节假日数据的年份一律不放
    public void Detect_GatesByHolidayAndLength(int year, int month, int day, CelebrationLevel expected)
        => Assert.Equal(expected, HolidayCelebration.Detect(new DateOnly(year, month, day)));
}
