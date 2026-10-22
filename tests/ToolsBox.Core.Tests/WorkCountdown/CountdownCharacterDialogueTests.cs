using System.Reflection;
using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.Core.Tests.WorkCountdown;

public sealed class CountdownCharacterDialogueTests
{
    [Theory]
    [InlineData("厘普", "啊？我刚才在吗？", "我看起来像会的吗？")]
    [InlineData("多栋", "我没摸鱼，我在等它上钩。", "没发呆，方案在脑子里跑。")]
    [InlineData("秀珍", "再戳算工伤。", "别催，在上了。")]
    [InlineData("田中", "有事说事，别盘我。", "工作很顺利，顺利到我这里。")]
    [InlineData("小河", "你点你的，我摆我的。", "收到，先放着。")]
    public void EachCharacter_HasApprovedClickAndFocusHover(string name, string click, string hover)
    {
        Assert.Equal(4, Dialogue.GetField("InteractionCount")!.GetRawConstantValue());
        Assert.Equal(click, Call("Click", name, 0));
        Assert.Equal(hover, Call("Hover", name, "focus"));
        string[] interactions = Enumerable.Range(0, 4).Select(sequence => Call("Click", name, sequence)).ToArray();
        Assert.Equal(4, interactions.Distinct().Count());
        Assert.All(interactions, text => Assert.InRange(text.Length, 1, 20));
    }

    [Theory]
    [InlineData("厘普")]
    [InlineData("多栋")]
    [InlineData("秀珍")]
    [InlineData("田中")]
    [InlineData("小河")]
    public void EveryStage_HasThreeShortDistinctDailyLines(string name)
    {
        foreach (string effect in new[] { "rest", "idle", "focus", "rush", "overtime", "overdue", "celebrate" })
        {
            string[] lines = Enumerable.Range(0, 3).Select(sequence => Call("Stage", name, effect, sequence)).ToArray();
            Assert.Equal(3, lines.Distinct().Count());
            Assert.All(lines, text => Assert.InRange(text.Length, 1, 20));
            Assert.InRange(Call("Hover", name, effect).Length, 1, 20);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Sequences_CycleSafelyAtBothIntegerLimits(int sequence)
    {
        int clickIndex = (int)(((long)sequence % 4 + 4) % 4);
        int stageIndex = (int)(((long)sequence % 3 + 3) % 3);

        Assert.Equal(Call("Click", "厘普", clickIndex), Call("Click", "厘普", sequence));
        Assert.Equal(Call("Stage", "田中", "focus", stageIndex), Call("Stage", "田中", "focus", sequence));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("陌生人", "陌生效果")]
    public void UnknownCharacterAndEffect_UseSafeShortFallbacks(string? name, string? effect)
    {
        Assert.InRange(Call("Stage", name, effect, int.MinValue).Length, 1, 20);
        Assert.InRange(Call("Hover", name, effect).Length, 1, 20);
        Assert.InRange(Call("Click", name, int.MaxValue).Length, 1, 20);
    }

    [Fact]
    public void StageSequence_HasADefaultAndDoesNotDependOnWallClock()
    {
        ParameterInfo sequence = Dialogue.GetMethod("Stage")!.GetParameters()[2];
        Assert.True(sequence.HasDefaultValue);
        Assert.Equal(0, sequence.DefaultValue);
        Assert.Equal(Call("Stage", "小河", "rest", 12), Call("Stage", "小河", "rest", 12));
    }

    private static Type Dialogue
    {
        get
        {
            Type? type = typeof(CountdownCardPresentation).Assembly.GetType("ToolsBox.Core.WorkCountdown.CountdownCharacterDialogue");
            Assert.NotNull(type);
            return type;
        }
    }

    private static string Call(string method, params object?[] arguments) =>
        Assert.IsType<string>(Dialogue.GetMethod(method)!.Invoke(null, arguments));
}
