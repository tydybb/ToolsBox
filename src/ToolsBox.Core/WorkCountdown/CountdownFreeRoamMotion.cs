namespace ToolsBox.Core.WorkCountdown;

/// <summary>Walks an entire companion group toward random points inside its measured area.</summary>
public sealed class CountdownFreeRoamMotion
{
    private const double MaximumTickSeconds = 0.25;
    private readonly double _pixelsPerSecond;
    private readonly double _pauseSeconds;
    private readonly Random _random;
    private double _remainingPause;
    private double _targetX;
    private double _targetY;
    private bool _hasPosition;

    public CountdownFreeRoamMotion(double pixelsPerSecond = 24, double pauseSeconds = 1.6, Random? random = null)
    {
        if (!double.IsFinite(pixelsPerSecond) || pixelsPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelsPerSecond));
        if (!double.IsFinite(pauseSeconds) || pauseSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(pauseSeconds));
        _pixelsPerSecond = pixelsPerSecond;
        _pauseSeconds = pauseSeconds;
        _random = random ?? Random.Shared;
        _remainingPause = pauseSeconds;
    }

    public double Position { get; private set; }
    public double VerticalPosition { get; private set; }
    public double MaximumX { get; private set; }
    public double MaximumY { get; private set; }
    public bool FacingRight { get; private set; } = true;
    public bool IsWalking { get; private set; }
    public bool CanMove { get; private set; }

    public void Resize(double areaWidth, double areaHeight, double groupWidth, double groupHeight)
    {
        bool valid = IsDimension(areaWidth) && IsDimension(areaHeight)
            && IsDimension(groupWidth) && IsDimension(groupHeight);
        MaximumX = valid ? Math.Max(0, areaWidth - groupWidth) : 0;
        MaximumY = valid ? Math.Max(0, areaHeight - groupHeight) : 0;
        CanMove = valid && areaWidth > groupWidth && areaHeight >= groupHeight;

        if (!CanMove)
        {
            Position = Math.Clamp(Position, 0, MaximumX);
            VerticalPosition = Math.Clamp(VerticalPosition, 0, MaximumY);
            _hasPosition = false;
            Rest();
            return;
        }
        if (!_hasPosition)
        {
            Position = MaximumX / 2;
            VerticalPosition = MaximumY / 2;
            _hasPosition = true;
            Rest();
            return;
        }

        double x = Math.Clamp(Position, 0, MaximumX);
        double y = Math.Clamp(VerticalPosition, 0, MaximumY);
        if (x != Position || y != VerticalPosition || _targetX > MaximumX || _targetY > MaximumY)
        {
            Position = x;
            VerticalPosition = y;
            Rest();
        }
    }

    public void Advance(TimeSpan elapsed)
    {
        if (!CanMove || elapsed <= TimeSpan.Zero) return;
        double seconds = Math.Min(elapsed.TotalSeconds, MaximumTickSeconds);
        if (!IsWalking)
        {
            double restingSeconds = Math.Min(_remainingPause, seconds);
            _remainingPause -= restingSeconds;
            seconds -= restingSeconds;
            if (_remainingPause > 0) return;
            SelectTarget();
            IsWalking = true;
        }
        if (seconds == 0) return;

        double deltaX = _targetX - Position;
        double deltaY = _targetY - VerticalPosition;
        double longestAxis = Math.Max(Math.Abs(deltaX), Math.Abs(deltaY));
        if (longestAxis == 0)
        {
            Rest();
            return;
        }
        double unitX = deltaX / longestAxis;
        double unitY = deltaY / longestAxis;
        double unitLength = Math.Sqrt(unitX * unitX + unitY * unitY);
        double step = _pixelsPerSecond * seconds;
        if (longestAxis <= step / unitLength)
        {
            Position = _targetX;
            VerticalPosition = _targetY;
            Rest();
        }
        else
        {
            Position = Math.Clamp(Position + step * unitX / unitLength, 0, MaximumX);
            VerticalPosition = Math.Clamp(VerticalPosition + step * unitY / unitLength, 0, MaximumY);
        }
    }

    private void SelectTarget()
    {
        // Avoid almost stationary targets so a walk has a visible direction on both available axes.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            _targetX = _random.NextDouble() * MaximumX;
            _targetY = _random.NextDouble() * MaximumY;
            if (Math.Abs(_targetX - Position) >= Math.Min(4, MaximumX * 0.1)
                && (MaximumY == 0 || Math.Abs(_targetY - VerticalPosition) >= Math.Min(4, MaximumY * 0.1)))
            {
                FacingRight = _targetX > Position;
                return;
            }
        }
        _targetX = Position < MaximumX / 2 ? MaximumX : 0;
        _targetY = VerticalPosition < MaximumY / 2 ? MaximumY : 0;
        FacingRight = _targetX > Position;
    }

    private void Rest()
    {
        IsWalking = false;
        _remainingPause = _pauseSeconds;
    }

    private static bool IsDimension(double value) => double.IsFinite(value) && value >= 0;
}
