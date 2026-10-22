using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class CountdownCompanionTests
{
    [Fact]
    public Task Companion_CharacterSizeUpdatesImageNameGroupAndMinimumAreaHeight() => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = CreateCompanion("lipu/calm", "厘普");
        var property = companion.GetType().GetProperty("CharacterSize");
        Assert.NotNull(property);
        Assert.Equal(128d, property.GetValue(companion));
        Window window = Host(companion, 360);
        try
        {
            window.Show();
            await SettleAsync();
            Set(companion, "CharacterSize", 80d);
            await SettleAsync();
            Image sticker = Get<Image>(companion, "Sticker");
            FrameworkElement group = Get<FrameworkElement>(companion, "MotionGroup");
            Assert.Equal(80, sticker.Width);
            Assert.Equal(80, sticker.Height);
            Assert.Equal(80, sticker.ActualWidth);
            Assert.Equal(80, sticker.ActualHeight);
            Assert.Equal(80, group.Width);
            Assert.Equal(102, companion.MinHeight);
            Assert.Equal(80, Get<TextBlock>(companion, "NameText").ActualWidth);
            Assert.InRange(Get<double>(companion, "Position"), 0, companion.ActualWidth - group.ActualWidth);
            Assert.InRange(Get<double>(companion, "VerticalPosition"), 0, companion.ActualHeight - group.ActualHeight);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(47)]
    [InlineData(257)]
    public Task Companion_CharacterSizeRejectsInvalidDimensions(double size) => WpfTestThread.RunAsync(() =>
    {
        FrameworkElement companion = CreateCompanion("lipu/calm", "厘普");
        var field = companion.GetType().GetField("CharacterSizeProperty", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        var property = Assert.IsType<DependencyProperty>(field.GetValue(null));
        Assert.Throws<ArgumentException>(() => companion.SetValue(property, size));
        Assert.Equal(128, Get<Image>(companion, "Sticker").Width);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Companion_CharacterSizeGrowthClampsExistingPositionAndResumesAfterShrinking() => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = CreateCompanion("lipu/calm", "厘普");
        Assert.NotNull(companion.GetType().GetProperty("CharacterSize"));
        Set(companion, "CharacterSize", 80d);
        Window window = Host(companion, 240);
        try
        {
            window.Show();
            await SettleAsync();
            Assert.True(Get<double>(companion, "Position") > 0);
            Set(companion, "CharacterSize", 256d);
            await SettleAsync();
            Assert.Equal(0, Get<double>(companion, "Position"));
            Assert.False(Get<bool>(companion, "IsMotionRunning"));
            Assert.False(Get<bool>(companion, "IsWalking"));
            Set(companion, "CharacterSize", 48d);
            await SettleAsync();
            Assert.Equal(70, companion.MinHeight);
            Assert.True(Get<bool>(companion, "IsMotionRunning"));
            FrameworkElement group = Get<FrameworkElement>(companion, "MotionGroup");
            Assert.InRange(Get<double>(companion, "Position"), 0, companion.ActualWidth - group.ActualWidth);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Companion_NameTravelsInsideSameGroupButOnlyImageMirrors() => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = CreateCompanion("lipu/calm", "厘普");
        Window window = Host(companion, 180);
        try
        {
            window.Show();
            await SettleAsync();
            FrameworkElement group = Get<FrameworkElement>(companion, "MotionGroup");
            Image sticker = Get<Image>(companion, "Sticker");
            TextBlock name = Get<TextBlock>(companion, "NameText");
            Assert.True(companion.ClipToBounds);
            Assert.Equal("厘普", name.Text);
            Assert.Same(group, VisualTreeHelper.GetParent(sticker));
            Assert.Same(group, VisualTreeHelper.GetParent(name));
            Assert.IsType<TranslateTransform>(group.RenderTransform);
            Assert.False(name.RenderTransform is ScaleTransform);
            Assert.False(group.RenderTransform is ScaleTransform);

            await WaitForAsync(() => Get<bool>(companion, "IsWalking"));
            Assert.Equal("lipu/walk-calm", Get<string>(sticker, "AnimationKey"));
            var first = group.TranslatePoint(new Point(), companion);
            await Task.Delay(180);
            var next = group.TranslatePoint(new Point(), companion);
            Assert.True((next - first).Length > 0);
            // A short random walk can finish during sampling; idle must restore the applied mood.
            Assert.Equal(Get<bool>(companion, "IsWalking") ? "lipu/walk-calm" : "lipu/calm",
                Get<string>(sticker, "AnimationKey"));
            double horizontalMovement = next.X - first.X;
            if (Math.Abs(horizontalMovement) > 0.01)
            {
                Assert.Equal(horizontalMovement > 0 ? 1 : -1,
                    Assert.IsType<ScaleTransform>(sticker.RenderTransform).ScaleX);
            }
            Assert.True(Get<double>(companion, "Position") >= 0);
            Assert.False(name.RenderTransform is ScaleTransform);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Companion_RoamsBothAxesKeepingImageAndNameInsideTheMeasuredArea() => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = CreateCompanion("lipu/calm", "厘普");
        Set(companion, "CharacterSize", 80d);
        Window window = Host(companion, 360);
        try
        {
            window.Show();
            await SettleAsync();
            FrameworkElement group = Get<FrameworkElement>(companion, "MotionGroup");
            Assert.NotNull(companion.GetType().GetProperty("VerticalPosition"));
            Assert.Equal((companion.ActualWidth - group.ActualWidth) / 2,
                Get<double>(companion, "Position"), 4);
            Assert.Equal((companion.ActualHeight - group.ActualHeight) / 2,
                Get<double>(companion, "VerticalPosition"), 4);

            await WaitForAsync(() => Get<bool>(companion, "IsWalking"));
            Point first = group.TranslatePoint(new Point(), companion);
            await Task.Delay(180);
            Point next = group.TranslatePoint(new Point(), companion);
            Assert.True(Math.Abs(next.X - first.X) > 0.01);
            Assert.True(Math.Abs(next.Y - first.Y) > 0.01);
            Assert.InRange((next - first).Length, 0.01, 12);
            AssertGroupInsideArea(companion);

            Set(companion, "AnimationEnabled", false);
            window.Width = 125;
            window.Height = 105;
            await SettleAsync();
            AssertGroupInsideArea(companion);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Companion_ResizingClampsMovementAndNarrowAreaRemainsIdle() => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = CreateCompanion("duodong/tired", "多栋");
        Window window = Host(companion, 240);
        try
        {
            window.Show();
            await SettleAsync();
            await WaitForAsync(() => Get<bool>(companion, "IsWalking"));
            window.Width = 140;
            await SettleAsync();
            await Task.Delay(100);
            FrameworkElement group = Get<FrameworkElement>(companion, "MotionGroup");
            Assert.InRange(Get<double>(companion, "Position"), 0, Math.Max(0, companion.ActualWidth - group.ActualWidth));
            window.Width = 100;
            await SettleAsync();
            Assert.False(Get<bool>(companion, "IsWalking"));
            Assert.False(Get<bool>(companion, "IsMotionRunning"));
            Assert.Equal("duodong/tired", Get<string>(Get<Image>(companion, "Sticker"), "AnimationKey"));
            window.Width = 240;
            await SettleAsync();
            Assert.True(Get<bool>(companion, "IsMotionRunning"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Companion_DisabledHiddenMinimizedAndUnloadedPauseBothMotionAndFrames() => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = CreateCompanion("xiuzhen/happy", "秀珍");
        Window window = Host(companion, 360);
        Window nextHost = Host(null, 360);
        try
        {
            window.Show();
            await SettleAsync();
            await WaitForAsync(() => Get<bool>(companion, "IsWalking"));

            Set(companion, "AnimationEnabled", false);
            await AssertStoppedAsync(companion);
            Set(companion, "AnimationEnabled", true);
            Assert.True(Get<bool>(companion, "IsMotionRunning"));
            window.WindowState = WindowState.Minimized;
            await SettleAsync();
            await AssertStoppedAsync(companion);
            window.WindowState = WindowState.Normal;
            await SettleAsync();
            Assert.True(Get<bool>(companion, "IsMotionRunning"));
            companion.Visibility = Visibility.Collapsed;
            await SettleAsync();
            await AssertStoppedAsync(companion);
            companion.Visibility = Visibility.Visible;
            await SettleAsync();
            Assert.True(Get<bool>(companion, "IsMotionRunning"));
            window.Hide();
            await SettleAsync();
            await AssertStoppedAsync(companion);
            window.Show();
            await SettleAsync();
            Assert.True(Get<bool>(companion, "IsMotionRunning"));

            window.Content = null;
            await SettleAsync();
            await AssertStoppedAsync(companion);
            nextHost.Content = companion;
            nextHost.Show();
            await SettleAsync();
            Assert.True(Get<bool>(companion, "IsMotionRunning"));
            window.WindowState = WindowState.Minimized;
            await SettleAsync();
            Assert.True(Get<bool>(companion, "IsMotionRunning"));
        }
        finally { window.Close(); nextHost.Close(); }
    });

    [Fact]
    public Task Companion_MissingWalkAnimationDoesNotSlideAnIdleSticker() => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = CreateCompanion("missing/calm", "测试角色");
        Window window = Host(companion, 360);
        try
        {
            window.Show();
            await SettleAsync();
            Assert.False(Get<bool>(companion, "IsMotionRunning"));
            Assert.False(Get<bool>(companion, "IsWalking"));
            Assert.Null(Get<Image>(companion, "Sticker").Source);
            await AssertStoppedAsync(companion);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Companion_ChangingMoodWhileWalkingPreservesMoodAndRestoresExpressionWhenPaused() => WpfTestThread.RunAsync(async () =>
    {
        FrameworkElement companion = CreateCompanion("xiaohe/calm", "小河");
        Window window = Host(companion, 360);
        try
        {
            window.Show();
            await SettleAsync();
            await WaitForAsync(() => Get<bool>(companion, "IsWalking"));
            Set(companion, "AnimationKey", "xiaohe/stressed");
            Set(companion, "CharacterName", "小河加班中");
            Image sticker = Get<Image>(companion, "Sticker");
            Assert.Equal("xiaohe/walk-stressed", Get<string>(sticker, "AnimationKey"));
            Assert.Equal("小河加班中", Get<TextBlock>(companion, "NameText").Text);
            Set(companion, "AnimationEnabled", false);
            Assert.Equal("xiaohe/stressed", Get<string>(sticker, "AnimationKey"));
            await AssertStoppedAsync(companion);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Companion_DarkThemeAddsOnlySubtleImageGlowWithoutCharacterBackground() => WpfTestThread.RunAsync(() =>
    {
        FrameworkElement companion = CreateCompanion("tianzhong/stressed", "田中");
        Image sticker = Get<Image>(companion, "Sticker");
        Assert.Null(sticker.Effect);
        Assert.Null(Assert.IsAssignableFrom<Panel>(companion).Background);
        Assert.Null(Assert.IsAssignableFrom<Panel>(Get<FrameworkElement>(companion, "MotionGroup")).Background);
        Set(companion, "IsDarkTheme", true);
        DropShadowEffect glow = Assert.IsType<DropShadowEffect>(sticker.Effect);
        Assert.Equal(0, glow.ShadowDepth);
        Assert.Equal(Colors.White, glow.Color);
        Assert.InRange(glow.BlurRadius, 1, 4);
        Assert.InRange(glow.Opacity, 0.1, 0.5);
        Assert.Null(Get<TextBlock>(companion, "NameText").Effect);
        Set(companion, "IsDarkTheme", false);
        Assert.Null(sticker.Effect);
        return Task.CompletedTask;
    });

    private static FrameworkElement CreateCompanion(string key, string name)
    {
        Type? type = typeof(App).Assembly.GetType("ToolsBox.App.WorkCountdown.CountdownCompanion");
        Assert.NotNull(type);
        // Motion behavior must not depend on the runner's Windows accessibility preference.
        var companion = Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(type!,
            BindingFlags.Instance | BindingFlags.NonPublic, null, [new Func<bool>(() => true)], null));
        Set(companion, "AnimationKey", key);
        Set(companion, "CharacterName", name);
        Assert.True(Get<bool>(companion, "AnimationEnabled"));
        return companion;
    }

    private static T Get<T>(object target, string property)
    {
        PropertyInfo? info = target.GetType().GetProperty(property);
        Assert.NotNull(info);
        return Assert.IsAssignableFrom<T>(info.GetValue(target));
    }

    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property)!.SetValue(target, value);

    private static Window Host(FrameworkElement? content, double width) => new()
    {
        Content = content, Width = width, Height = 180, WindowStyle = WindowStyle.None,
        ShowInTaskbar = false, ShowActivated = false, Opacity = 0
    };

    private static async Task SettleAsync() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

    private static async Task WaitForAsync(Func<bool> condition, int seconds = 4)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(15);
        Assert.True(condition());
    }

    private static async Task AssertStoppedAsync(FrameworkElement companion)
    {
        Assert.False(Get<bool>(companion, "IsMotionRunning"));
        Assert.False(Get<bool>(companion, "IsWalking"));
        double position = Get<double>(companion, "Position");
        double verticalPosition = Get<double>(companion, "VerticalPosition");
        Image sticker = Get<Image>(companion, "Sticker");
        ImageSource? frame = sticker.Source;
        await Task.Delay(180);
        Assert.Equal(position, Get<double>(companion, "Position"));
        Assert.Equal(verticalPosition, Get<double>(companion, "VerticalPosition"));
        Assert.Same(frame, sticker.Source);
        Assert.False(Get<bool>(sticker, "IsPlaying"));
    }

    private static void AssertGroupInsideArea(FrameworkElement companion)
    {
        FrameworkElement group = Get<FrameworkElement>(companion, "MotionGroup");
        Rect bounds = group.TransformToAncestor(companion).TransformBounds(new Rect(group.RenderSize));
        Assert.InRange(bounds.Left, 0, companion.ActualWidth);
        Assert.InRange(bounds.Top, 0, companion.ActualHeight);
        Assert.InRange(bounds.Right, 0, companion.ActualWidth + 0.01);
        Assert.InRange(bounds.Bottom, 0, companion.ActualHeight + 0.01);
    }
}
