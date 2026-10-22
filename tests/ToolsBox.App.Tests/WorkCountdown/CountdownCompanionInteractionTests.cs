using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class CountdownCompanionInteractionTests
{
    [Theory]
    [InlineData("厘普", "啊？我刚才在吗？")]
    [InlineData("多栋", "我没摸鱼，我在等它上钩。")]
    [InlineData("秀珍", "再戳算工伤。")]
    [InlineData("田中", "有事说事，别盘我。")]
    [InlineData("小河", "你点你的，我摆我的。")]
    public Task CharacterClick_UsesThatCharactersOwnShortVoice(string name, string speech) => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = Create(() => false);
        Set(companion, "CharacterName", name);
        Set(companion, "SpeechText", "计时照常进行。");
        var window = Host(companion);
        try
        {
            window.Show();
            await SettleAsync();
            Click(Get<Image>(companion, "Sticker"));
            Assert.Equal(speech, Get<TextBlock>(companion, "SpeechBubbleText").Text);
            Assert.Equal("计时照常进行。", Get<string>(companion, "SpeechText"));
            Assert.Equal(name, Get<TextBlock>(companion, "NameText").Text);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("厘普", "我看起来像会的吗？")]
    [InlineData("多栋", "没发呆，方案在脑子里跑。")]
    [InlineData("秀珍", "别催，在上了。")]
    [InlineData("田中", "工作很顺利，顺利到我这里。")]
    [InlineData("小河", "收到，先放着。")]
    public Task CharacterHover_UsesThatCharactersOwnShortVoice(string name, string speech) => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = Create(() => false);
        Set(companion, "CharacterName", name);
        Set(companion, "EffectKey", "focus");
        Set(companion, "SpeechText", "计时照常进行。");
        var window = Host(companion);
        try
        {
            window.Show();
            await SettleAsync();
            Get<Image>(companion, "Sticker").RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                { RoutedEvent = Mouse.MouseEnterEvent });
            Assert.Equal(speech, Get<TextBlock>(companion, "SpeechBubbleText").Text);
            Assert.Equal("计时照常进行。", Get<string>(companion, "SpeechText"));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CharacterNameChange_RefreshesTheActiveVoiceWithoutChangingTheAppliedSpeech(bool clicked) => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = Create(() => false);
        Set(companion, "EffectKey", "focus");
        Set(companion, "SpeechText", "计时照常进行。");
        var window = Host(companion);
        try
        {
            window.Show();
            await SettleAsync();
            Image sticker = Get<Image>(companion, "Sticker");
            if (clicked) Click(sticker);
            else sticker.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            string previous = Get<TextBlock>(companion, "SpeechBubbleText").Text;
            Set(companion, "CharacterName", "田中");
            Assert.NotEqual(previous, Get<TextBlock>(companion, "SpeechBubbleText").Text);
            Assert.Equal(clicked ? "有事说事，别盘我。" : "工作很顺利，顺利到我这里。",
                Get<TextBlock>(companion, "SpeechBubbleText").Text);
            Assert.Equal("计时照常进行。", Get<string>(companion, "SpeechText"));
            Assert.Equal("田中", Get<TextBlock>(companion, "NameText").Text);
            if (clicked)
            {
                Click(sticker);
                Assert.NotEqual("有事说事，别盘我。", Get<TextBlock>(companion, "SpeechBubbleText").Text);
                await WaitForAsync(() => Get<TextBlock>(companion, "SpeechBubbleText").Text == "计时照常进行。", 4);
            }
            else
            {
                sticker.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                Assert.Equal("计时照常进行。", Get<TextBlock>(companion, "SpeechBubbleText").Text);
            }
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(320, 160)]
    [InlineData(180, 160)]
    [InlineData(220, 120)]
    public Task SpeechBubble_IsAnIndependentBoundedThemeAwareOverlay(double width, double height) => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = Create();
        Set(companion, "SpeechText", "下班导航已开启，快乐马上到站。");
        Set(companion, "AnimationEnabled", false);
        var window = Host(companion, width, height);
        try
        {
            window.Show();
            await SettleAsync();
            var group = Get<FrameworkElement>(companion, "MotionGroup");
            var bubble = Get<Border>(companion, "SpeechBubble");
            var text = Get<TextBlock>(companion, "SpeechBubbleText");
            Assert.Equal("下班导航已开启，快乐马上到站。", text.Text);
            Assert.Equal(Visibility.Visible, bubble.Visibility);
            Assert.False(bubble.IsHitTestVisible);
            Assert.NotSame(group, VisualTreeHelper.GetParent(bubble));
            Assert.Equal(2, Assert.IsType<StackPanel>(group).Children.Count);
            Assert.Equal(80, group.ActualWidth);
            Assert.InRange(group.ActualHeight, 98, 106);
            AssertInside(companion, bubble);
            Assert.Null(Assert.IsAssignableFrom<Panel>(companion).Background);
            Assert.Null(Assert.IsAssignableFrom<Panel>(group).Background);
            Assert.Same(window.Resources["ToolboxInfoSurfaceBrush"], bubble.Background);
            Assert.Same(window.Resources["ToolboxTextBrush"], text.Foreground);
            window.Resources["ToolboxInfoSurfaceBrush"] = Brushes.DarkSlateBlue;
            window.Resources["ToolboxTextBrush"] = Brushes.WhiteSmoke;
            await SettleAsync();
            Assert.Same(Brushes.DarkSlateBlue, bubble.Background);
            Assert.Same(Brushes.WhiteSmoke, text.Foreground);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(220, 160)]
    [InlineData(220, 120)]
    [InlineData(180, 160)]
    public Task DenseSpeechBubble_DoesNotCoverTheCharacterOrItsName(double width, double height) => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = Create();
        Set(companion, "SpeechText", "摸鱼搭子陪你认真等下班快乐悄悄装进你口袋");
        Set(companion, "AnimationEnabled", false);
        var window = Host(companion, width, height);
        try
        {
            window.Show();
            await SettleAsync();
            var bubble = Get<Border>(companion, "SpeechBubble");
            var group = Get<FrameworkElement>(companion, "MotionGroup");
            var name = Get<TextBlock>(companion, "NameText");
            Assert.Equal(20, Get<TextBlock>(companion, "SpeechBubbleText").Text.Length);
            Assert.Equal(Visibility.Visible, bubble.Visibility);
            Rect bubbleBounds = bubble.TransformToAncestor(companion).TransformBounds(new Rect(bubble.RenderSize));
            Rect groupBounds = group.TransformToAncestor(companion).TransformBounds(new Rect(group.RenderSize));
            Rect nameBounds = name.TransformToAncestor(companion).TransformBounds(new Rect(name.RenderSize));
            Assert.False(bubbleBounds.IntersectsWith(groupBounds));
            Assert.False(bubbleBounds.IntersectsWith(nameBounds));
            AssertInside(companion, bubble);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CharacterHover_PausesWalkingAndBlankAreaDoesNotRespond() => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = Create();
        Set(companion, "SpeechText", "正在认真等下班。");
        var window = Host(companion);
        try
        {
            window.Show();
            await SettleAsync();
            await WaitForAsync(() => Get<bool>(companion, "IsWalking"));
            companion.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            Assert.Equal("正在认真等下班。", Get<TextBlock>(companion, "SpeechBubbleText").Text);
            var sticker = Get<Image>(companion, "Sticker");
            sticker.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            Assert.False(Get<bool>(companion, "IsMotionRunning"));
            Assert.False(Get<bool>(companion, "IsWalking"));
            Assert.NotEqual("正在认真等下班。", Get<TextBlock>(companion, "SpeechBubbleText").Text);
            double x = Get<double>(companion, "Position"), y = Get<double>(companion, "VerticalPosition");
            await Task.Delay(180);
            Assert.Equal(x, Get<double>(companion, "Position"));
            Assert.Equal(y, Get<double>(companion, "VerticalPosition"));
            Assert.Equal("lipu/calm", Get<string>(sticker, "AnimationKey"));
            sticker.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseMoveEvent });
            Assert.Equal(Mouse.GetPosition(companion).X >= x + 40 ? 1 : -1,
                Assert.IsType<ScaleTransform>(sticker.RenderTransform).ScaleX);
            sticker.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            Assert.True(Get<bool>(companion, "IsMotionRunning"));
            Assert.Equal("正在认真等下班。", Get<TextBlock>(companion, "SpeechBubbleText").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CharacterClick_CyclesSpeechUsesHappyThenReturnsToLatestAppliedMood() => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = Create();
        Set(companion, "SpeechText", "先稳住，再下班。");
        var window = Host(companion);
        try
        {
            window.Show();
            await SettleAsync();
            Image sticker = Get<Image>(companion, "Sticker");
            companion.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
            Assert.Equal("先稳住，再下班。", Get<TextBlock>(companion, "SpeechBubbleText").Text);
            Click(sticker);
            string first = Get<TextBlock>(companion, "SpeechBubbleText").Text;
            Assert.NotEqual("先稳住，再下班。", first);
            Assert.Equal("lipu/happy", Get<string>(sticker, "AnimationKey"));
            Assert.False(Get<bool>(companion, "IsMotionRunning"));
            Click(sticker);
            Assert.NotEqual(first, Get<TextBlock>(companion, "SpeechBubbleText").Text);
            Set(companion, "AnimationKey", "lipu/stressed");
            Assert.Equal("lipu/happy", Get<string>(sticker, "AnimationKey"));
            await WaitForAsync(() => Get<TextBlock>(companion, "SpeechBubbleText").Text == "先稳住，再下班。", 4);
            Assert.True(Get<bool>(companion, "IsMotionRunning"));
            Assert.Contains(Get<string>(sticker, "AnimationKey"), new[] { "lipu/stressed", "lipu/walk-stressed" });
            Assert.Equal("lipu/stressed", Get<string>(companion, "AnimationKey"));
            Assert.Same(Get<FrameworkElement>(companion, "MotionGroup"), VisualTreeHelper.GetParent(Get<TextBlock>(companion, "NameText")));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public Task CharacterKeyboardActivation_ProvidesStaticInteractionWhenAnimationIsDisabled(Key key) => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = Create();
        Set(companion, "SpeechText", "稳稳待机。");
        Set(companion, "AnimationEnabled", false);
        var window = Host(companion);
        try
        {
            window.Show();
            await SettleAsync();
            Image sticker = Get<Image>(companion, "Sticker");
            Assert.True(sticker.Focusable);
            Assert.Null(sticker.ToolTip);
            Assert.Contains("厘普", AutomationProperties.GetName(sticker));
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(sticker), 0, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
            sticker.RaiseEvent(args);
            Assert.True(args.Handled);
            Assert.NotEqual("稳稳待机。", Get<TextBlock>(companion, "SpeechBubbleText").Text);
            Assert.Equal("lipu/happy", Get<string>(sticker, "AnimationKey"));
            Assert.False(Get<bool>(sticker, "IsPlaying"));
            Assert.False(Get<bool>(companion, "IsMotionRunning"));
            Assert.Empty(Get<Canvas>(companion, "EffectLayer").Children.Cast<UIElement>());
            await WaitForAsync(() => Get<TextBlock>(companion, "SpeechBubbleText").Text == "稳稳待机。", 4);
            Assert.Equal("lipu/calm", Get<string>(sticker, "AnimationKey"));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("rest")]
    [InlineData("focus")]
    [InlineData("rush")]
    [InlineData("overtime")]
    [InlineData("overdue")]
    [InlineData("celebrate")]
    public Task StageTransition_ProducesOneFiniteBoundedNonInteractiveBurst(string stage) => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = Create();
        var window = Host(companion, 220, 160);
        try
        {
            window.Show();
            await SettleAsync();
            Set(companion, "EffectKey", stage);
            await SettleAsync();
            var layer = Get<Canvas>(companion, "EffectLayer");
            Assert.False(layer.IsHitTestVisible);
            Assert.True(layer.ClipToBounds);
            Assert.InRange(layer.Children.Count, 1, 24);
            foreach (FrameworkElement particle in layer.Children) AssertInside(companion, particle);
            await Task.Delay(200);
            window.Width = 150;
            await SettleAsync();
            foreach (FrameworkElement particle in layer.Children) AssertInside(companion, particle);
            await WaitForAsync(() => layer.Children.Count == 0, 3);
            await Task.Delay(160);
            Assert.Empty(layer.Children.Cast<UIElement>());
            Set(companion, "EffectKey", stage);
            Assert.Empty(layer.Children.Cast<UIElement>());
            Set(companion, "EffectKey", "idle");
            Assert.Empty(layer.Children.Cast<UIElement>());
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("hidden")]
    [InlineData("minimized")]
    [InlineData("unloaded")]
    public Task Suspension_CancelsClickFeedbackAndRestoresAppliedMood(string suspension) => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = Create();
        Set(companion, "SpeechText", "工位待机中。");
        var window = Host(companion);
        try
        {
            window.Show();
            await SettleAsync();
            Image sticker = Get<Image>(companion, "Sticker");
            Click(sticker);
            Assert.Equal("lipu/happy", Get<string>(sticker, "AnimationKey"));
            switch (suspension)
            {
                case "hidden": window.Hide(); break;
                case "minimized": window.WindowState = WindowState.Minimized; break;
                case "unloaded": window.Content = null; break;
            }
            await SettleAsync();
            Assert.Equal("工位待机中。", Get<TextBlock>(companion, "SpeechBubbleText").Text);
            Assert.Equal("lipu/calm", Get<string>(sticker, "AnimationKey"));
            Assert.Empty(Get<Canvas>(companion, "EffectLayer").Children.Cast<UIElement>());
            Assert.False(Get<bool>(companion, "IsMotionRunning"));
            Assert.False(Get<bool>(sticker, "IsPlaying"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ReducedMotion_StopsFramesMovementAndEffectsButKeepsStaticClickSpeech() => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = Create(() => false);
        Set(companion, "SpeechText", "今天也陪你等下班。");
        Set(companion, "EffectKey", "celebrate");
        var window = Host(companion);
        try
        {
            window.Show();
            await SettleAsync();
            Image sticker = Get<Image>(companion, "Sticker");
            Assert.False(Get<bool>(companion, "IsMotionRunning"));
            Assert.False(Get<bool>(sticker, "IsPlaying"));
            Assert.Empty(Get<Canvas>(companion, "EffectLayer").Children.Cast<UIElement>());
            Click(sticker);
            Assert.NotEqual("今天也陪你等下班。", Get<TextBlock>(companion, "SpeechBubbleText").Text);
            Assert.Equal("lipu/happy", Get<string>(sticker, "AnimationKey"));
            Assert.False(Get<bool>(sticker, "IsPlaying"));
            Assert.Empty(Get<Canvas>(companion, "EffectLayer").Children.Cast<UIElement>());
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("disabled")]
    [InlineData("hidden")]
    [InlineData("minimized")]
    [InlineData("unloaded")]
    public Task Suspension_ClearsParticlesAndStopsEveryAnimation(string suspension) => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = Create();
        var window = Host(companion);
        try
        {
            window.Show();
            await SettleAsync();
            Set(companion, "EffectKey", "celebrate");
            Assert.InRange(Get<Canvas>(companion, "EffectLayer").Children.Count, 1, 24);
            switch (suspension)
            {
                case "disabled": Set(companion, "AnimationEnabled", false); break;
                case "hidden": companion.Visibility = Visibility.Collapsed; break;
                case "minimized": window.WindowState = WindowState.Minimized; break;
                case "unloaded": window.Content = null; break;
            }
            await SettleAsync();
            Assert.Empty(Get<Canvas>(companion, "EffectLayer").Children.Cast<UIElement>());
            Assert.False(Get<bool>(companion, "IsMotionRunning"));
            Image sticker = Get<Image>(companion, "Sticker");
            Assert.False(Get<bool>(sticker, "IsPlaying"));
            ImageSource? frame = sticker.Source;
            double x = Get<double>(companion, "Position"), y = Get<double>(companion, "VerticalPosition");
            await Task.Delay(180);
            Assert.Same(frame, sticker.Source);
            Assert.Equal(x, Get<double>(companion, "Position"));
            Assert.Equal(y, Get<double>(companion, "VerticalPosition"));
        }
        finally { window.Close(); }
    });

    private static FrameworkElement Create(Func<bool>? animationPreference = null)
    {
        Type? type = typeof(App).Assembly.GetType("ToolsBox.App.WorkCountdown.CountdownCompanion");
        Assert.NotNull(type);
        // Select the policy in the test; explicit reduced-motion cases still inject false.
        animationPreference ??= () => true;
        object? instance = Activator.CreateInstance(type!, BindingFlags.Instance | BindingFlags.NonPublic,
            null, [animationPreference], null);
        var companion = Assert.IsAssignableFrom<FrameworkElement>(instance);
        Set(companion, "AnimationKey", "lipu/calm");
        Set(companion, "CharacterName", "厘普");
        Set(companion, "CharacterSize", 80d);
        return companion;
    }

    private static T Get<T>(object target, string name)
    {
        PropertyInfo? property = target.GetType().GetProperty(name);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<T>(property.GetValue(target));
    }

    private static void Set(object target, string name, object value)
    {
        PropertyInfo? property = target.GetType().GetProperty(name);
        Assert.NotNull(property);
        property.SetValue(target, value);
    }

    private static void Click(Image sticker) => sticker.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
    {
        RoutedEvent = UIElement.MouseLeftButtonUpEvent
    });

    private static Window Host(FrameworkElement companion, double width = 320, double height = 160)
    {
        var window = new Window
        {
            Content = companion, Width = width, Height = height, WindowStyle = WindowStyle.None,
            ShowInTaskbar = false, ShowActivated = false, Opacity = 0
        };
        window.Resources["ToolboxTextBrush"] = Brushes.MidnightBlue;
        window.Resources["ToolboxInfoSurfaceBrush"] = Brushes.AliceBlue;
        window.Resources["ToolboxBorderBrush"] = Brushes.LightSteelBlue;
        window.Resources["ToolboxAccentBrush"] = Brushes.RoyalBlue;
        return window;
    }

    private static void AssertInside(FrameworkElement companion, FrameworkElement element)
    {
        Rect bounds = element.TransformToAncestor(companion).TransformBounds(new Rect(element.RenderSize));
        Assert.InRange(bounds.Left, 0, companion.ActualWidth + 0.01);
        Assert.InRange(bounds.Top, 0, companion.ActualHeight + 0.01);
        Assert.InRange(bounds.Right, 0, companion.ActualWidth + 0.01);
        Assert.InRange(bounds.Bottom, 0, companion.ActualHeight + 0.01);
    }

    private static async Task SettleAsync() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

    private static async Task WaitForAsync(Func<bool> condition, int seconds = 4)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.True(condition());
    }
}
