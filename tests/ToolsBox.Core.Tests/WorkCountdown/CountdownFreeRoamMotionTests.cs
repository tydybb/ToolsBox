using System.Reflection;
using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.Core.Tests.WorkCountdown;

public sealed class CountdownFreeRoamMotionTests
{
    [Fact]
    public void FirstMeasuredArea_CentersTheEntireGroupAndStartsResting()
    {
        var motion = new Motion();
        motion.Resize(360, 180, 80, 102);

        Assert.Equal(280, motion.MaximumX);
        Assert.Equal(78, motion.MaximumY);
        Assert.Equal(140, motion.X);
        Assert.Equal(39, motion.Y);
        Assert.False(motion.IsWalking);
    }

    [Fact]
    public void Resting_ConsumesOnlyActiveTimeBeforeSelectingATarget()
    {
        var motion = new Motion(pauseSeconds: 0.5);
        motion.Resize(360, 180, 80, 102);
        motion.Advance(TimeSpan.FromMilliseconds(250));
        Assert.False(motion.IsWalking);
        Assert.Equal(140, motion.X);
        Assert.Equal(39, motion.Y);
        motion.Advance(TimeSpan.FromMilliseconds(250));
        Assert.True(motion.IsWalking);
        Assert.Equal(140, motion.X);
        Assert.Equal(39, motion.Y);
        motion.Advance(TimeSpan.FromMilliseconds(250));
        Assert.NotEqual(140, motion.X);
        Assert.NotEqual(39, motion.Y);
    }

    [Fact]
    public void Walking_UsesOneNormalSpeedAcrossBothAxesAndFacesItsTarget()
    {
        var motion = new Motion(pauseSeconds: 0);
        motion.Resize(360, 180, 80, 102);

        motion.Advance(TimeSpan.FromMilliseconds(250));

        Assert.Equal(6, Distance(140, 39, motion.X, motion.Y), 6);
        Assert.NotEqual(140, motion.X);
        Assert.NotEqual(39, motion.Y);
        Assert.Equal(motion.X > 140, motion.FacingRight);
    }

    [Fact]
    public void RestEndingMidTick_MovesOnlyForTheRemainingTime()
    {
        var motion = new Motion(pauseSeconds: 0.1);
        motion.Resize(360, 180, 80, 102);
        motion.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Equal(3.6, Distance(140, 39, motion.X, motion.Y), 6);
    }

    [Fact]
    public void RandomTargets_ChangeDirectionAndPauseWithinTheArea()
    {
        var motion = new Motion(pauseSeconds: 0.4);
        motion.Resize(360, 180, 80, 102);
        bool sawLeft = false, sawRight = false, sawUp = false, sawDown = false, sawArrivalRest = false;
        for (int tick = 0; tick < 1500; tick++)
        {
            double x = motion.X, y = motion.Y;
            bool wasWalking = motion.IsWalking;
            motion.Advance(TimeSpan.FromMilliseconds(40));
            Assert.InRange(motion.X, 0, motion.MaximumX);
            Assert.InRange(motion.Y, 0, motion.MaximumY);
            Assert.InRange(Distance(x, y, motion.X, motion.Y), 0, 0.960001);
            sawLeft |= motion.X < x;
            sawRight |= motion.X > x;
            sawUp |= motion.Y < y;
            sawDown |= motion.Y > y;
            if (wasWalking && !motion.IsWalking)
            {
                sawArrivalRest = true;
                x = motion.X; y = motion.Y;
                motion.Advance(TimeSpan.FromMilliseconds(200));
                Assert.False(motion.IsWalking);
                Assert.Equal(x, motion.X);
                Assert.Equal(y, motion.Y);
            }
        }
        Assert.True(sawLeft && sawRight && sawUp && sawDown && sawArrivalRest);
    }

    [Fact]
    public void ShrinkingArea_ImmediatelyClampsBothCoordinatesAndRests()
    {
        var motion = new Motion(pauseSeconds: 0);
        motion.Resize(360, 180, 80, 102);
        motion.Advance(TimeSpan.FromMilliseconds(250));
        motion.Resize(100, 110, 80, 102);
        Assert.Equal(20, motion.X);
        Assert.Equal(8, motion.Y);
        Assert.False(motion.IsWalking);
    }

    [Fact]
    public void NormalResize_PreservesThePositionWithoutRecenteringOrTeleporting()
    {
        var motion = new Motion(pauseSeconds: 0);
        motion.Resize(360, 180, 80, 102);
        motion.Advance(TimeSpan.FromMilliseconds(250));
        double x = motion.X, y = motion.Y;
        motion.Resize(420, 240, 80, 102);
        Assert.Equal(x, motion.X);
        Assert.Equal(y, motion.Y);
    }

    [Theory]
    [InlineData(70, 180, 80, 102)]
    [InlineData(80, 180, 80, 102)]
    [InlineData(360, 90, 80, 102)]
    [InlineData(0, 0, 80, 102)]
    [InlineData(double.NaN, 180, 80, 102)]
    [InlineData(360, double.PositiveInfinity, 80, 102)]
    [InlineData(360, 180, -1, 102)]
    [InlineData(360, 180, 80, double.NaN)]
    public void InvalidOrInsufficientArea_StaysFiniteAndIdle(double width, double height, double groupWidth, double groupHeight)
    {
        var motion = new Motion(pauseSeconds: 0);
        motion.Resize(width, height, groupWidth, groupHeight);
        motion.Advance(TimeSpan.FromDays(30));
        Assert.True(double.IsFinite(motion.X));
        Assert.True(double.IsFinite(motion.Y));
        Assert.InRange(motion.X, 0, motion.MaximumX);
        Assert.InRange(motion.Y, 0, motion.MaximumY);
        Assert.False(motion.IsWalking);
        Assert.False(motion.CanMove);
    }

    [Fact]
    public void ReturningFromAnInsufficientArea_CentersAndRestsAgain()
    {
        var motion = new Motion(pauseSeconds: 0);
        motion.Resize(100, 180, 128, 150);
        motion.Resize(360, 180, 80, 102);
        Assert.Equal(140, motion.X);
        Assert.Equal(39, motion.Y);
        Assert.False(motion.IsWalking);
        Assert.True(motion.CanMove);
    }

    [Fact]
    public void LongElapsedTime_IsCappedToANormalTickInsteadOfJumpingAfterInactivity()
    {
        var motion = new Motion(pauseSeconds: 0);
        motion.Resize(360, 180, 80, 102);
        motion.Advance(TimeSpan.MaxValue);
        Assert.Equal(6, Distance(140, 39, motion.X, motion.Y), 6);
    }

    [Fact]
    public void NonPositiveElapsedTime_DoesNotMoveOrChooseATarget()
    {
        var motion = new Motion(pauseSeconds: 0);
        motion.Resize(360, 180, 80, 102);
        motion.Advance(TimeSpan.Zero);
        motion.Advance(TimeSpan.FromSeconds(-1));
        Assert.Equal(140, motion.X);
        Assert.Equal(39, motion.Y);
        Assert.False(motion.IsWalking);
    }

    private static double Distance(double x, double y, double nextX, double nextY) =>
        Math.Sqrt(Math.Pow(nextX - x, 2) + Math.Pow(nextY - y, 2));

    // Resolve the proposed public contract without making a missing engine a compiler error.
    private sealed class Motion
    {
        private readonly object _instance;
        private readonly Type _type;

        public Motion(double pauseSeconds = 1.6)
        {
            Type? type = typeof(CountdownCalculator).Assembly.GetType("ToolsBox.Core.WorkCountdown.CountdownFreeRoamMotion");
            Assert.NotNull(type);
            _type = type;
            ConstructorInfo? constructor = type.GetConstructor([typeof(double), typeof(double), typeof(Random)]);
            Assert.NotNull(constructor);
            _instance = constructor.Invoke([24d, pauseSeconds, new Random(173)]);
        }

        public double X => Get<double>("Position");
        public double Y => Get<double>("VerticalPosition");
        public double MaximumX => Get<double>(nameof(MaximumX));
        public double MaximumY => Get<double>(nameof(MaximumY));
        public bool FacingRight => Get<bool>(nameof(FacingRight));
        public bool IsWalking => Get<bool>(nameof(IsWalking));
        public bool CanMove => Get<bool>(nameof(CanMove));

        public void Resize(double width, double height, double groupWidth, double groupHeight) =>
            _type.GetMethod(nameof(Resize))!.Invoke(_instance, [width, height, groupWidth, groupHeight]);

        public void Advance(TimeSpan elapsed) => _type.GetMethod(nameof(Advance))!.Invoke(_instance, [elapsed]);
        private T Get<T>(string property) => Assert.IsType<T>(_type.GetProperty(property)!.GetValue(_instance));
    }
}
