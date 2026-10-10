using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using ToolsBox.App.Infrastructure;
using ToolsBox.App.WorkCountdown;
using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.App.Views;

public partial class OffWorkReminderWindow : Window
{
    /// <summary>纸屑与烟花统一的节庆配色，适用于深浅主题。</summary>
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0xFF, 0xD4, 0x47), // 金
        Color.FromRgb(0xFF, 0x87, 0x87), // 珊瑚红
        Color.FromRgb(0xFF, 0xFF, 0xFF), // 白
        Color.FromRgb(0x63, 0xE6, 0xBE), // 薄荷绿
        Color.FromRgb(0xFA, 0xA2, 0xC1), // 粉
        Color.FromRgb(0xFF, 0xA9, 0x4D)  // 橙
    ];

    /// <summary>完整版三次烟花的相对时刻（秒）。</summary>
    private static readonly double[] FireworkSeconds = [0.35, 1.35, 2.35];

    private const double TickSeconds = 1.0 / 30.0;
    private const double TotalSeconds = 6.0;
    private const double SparkSeconds = 1.5;
    private const int FullConfettiCount = 70;
    private const int LightConfettiCount = 44;
    private const int SparksPerFirework = 26;
    private const double SwayAmplitude = 14.0;

    private readonly List<Flake> _flakes = [];
    private readonly List<Spark> _sparks = [];
    private readonly WorkCountdownViewModel _countdown;
    private DispatcherTimer? _celebration;
    private double _elapsed;
    private int _nextFirework;
    private bool _closed;
    private bool _isDarkTheme;

    public OffWorkReminderWindow(WorkCountdownViewModel countdown)
        : this(countdown, ComfortAppearance.LoadDarkPreference()) { }

    public OffWorkReminderWindow(WorkCountdownViewModel countdown, bool dark)
    {
        InitializeComponent();
        DataContext = countdown;
        _countdown = countdown;
        SelectedArtworkKey = OffWorkArtworkCatalog.Select(DateOnly.FromDateTime(DateTime.Now));
        ReminderArtworkImage.Source = OffWorkArtworkCatalog.GetImage(SelectedArtworkKey);
        ApplyTheme(dark);
        SourceInitialized += (_, _) => ComfortAppearance.UpdateCaption(this, _isDarkTheme);
        // 点击本窗的“下班”（或倒计时卡片上的“下班”）后，这扇“即将关机”警告已无意义，随任务结束自动关闭。
        _countdown.PropertyChanged += OnCountdownPropertyChanged;
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _closed = true;
            _countdown.PropertyChanged -= OnCountdownPropertyChanged;
            StopCelebration();
        };
    }

    public string SelectedArtworkKey { get; }

    public void ApplyTheme(bool dark)
    {
        _isDarkTheme = dark;
        ComfortAppearance.Apply(this, dark);
        if (dark)
        {
            var glow = new DropShadowEffect
            {
                Color = Color.FromRgb(0xEC, 0xF0, 0xF6), ShadowDepth = 0,
                BlurRadius = 6, Opacity = 0.22, RenderingBias = RenderingBias.Quality
            };
            glow.Freeze();
            ReminderArtworkImage.Effect = glow;
        }
        else ReminderArtworkImage.Effect = null;
    }

    private void OnCountdownPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 主窗口在 IsOverdue 变 false 时也会关一次本窗，这里只负责补位（后台代理宿主没有该逻辑）。
        if (_closed || e.PropertyName != nameof(WorkCountdownViewModel.IsFinished) || !_countdown.IsFinished) return;
        Close();
    }

    private void OnDismiss(object sender, RoutedEventArgs e) => Close();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 只是庆祝外观，不调用任何系统效果；用户关掉系统动画或开高对比度时直接不放。
        if (SystemParameters.HighContrast || !SystemParameters.ClientAreaAnimation) return;
        StartCelebration(_countdown.CelebrationLevel);
    }

    /// <summary>
    /// 在本窗上放一段下班庆祝特效：Light 只撒纸屑，Full 再叠三朵烟花，None 什么都不做。
    /// 整段约 6 秒，跑完自动清理；再次调用会先清掉上一段。测试直接调用本方法，不依赖真实时钟与系统动画设置。
    /// </summary>
    public void StartCelebration(CelebrationLevel level)
    {
        StopCelebration();
        if (level == CelebrationLevel.None) return;

        SpawnConfetti(level == CelebrationLevel.Full ? FullConfettiCount : LightConfettiCount);
        _elapsed = 0;
        _nextFirework = 0;
        _celebration = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
        _celebration.Tick += OnCelebrationTick;
        _celebration.Start();
    }

    private void StopCelebration()
    {
        if (_celebration is not null)
        {
            _celebration.Stop();
            _celebration.Tick -= OnCelebrationTick;
            _celebration = null;
        }
        _flakes.Clear();
        _sparks.Clear();
        CelebrationCanvas.Children.Clear();
    }

    private void OnCelebrationTick(object? sender, EventArgs e)
    {
        _elapsed += TickSeconds;

        if (_nextFirework < FireworkSeconds.Length && _elapsed >= FireworkSeconds[_nextFirework])
        {
            SpawnFirework();
            _nextFirework++;
        }

        double height = CanvasHeight();

        for (int i = _flakes.Count - 1; i >= 0; i--)
        {
            Flake flake = _flakes[i];
            flake.Y += flake.Fall * TickSeconds;
            if (flake.Y > height + 24)
            {
                CelebrationCanvas.Children.Remove(flake.Shape);
                _flakes.RemoveAt(i);
                continue;
            }
            Canvas.SetTop(flake.Shape, flake.Y);
            Canvas.SetLeft(flake.Shape, flake.BaseX + Math.Sin(_elapsed * flake.Sway + flake.Phase) * SwayAmplitude);
            flake.Spin.Angle = _elapsed * flake.SpinSpeed % 360;
        }

        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            Spark spark = _sparks[i];
            spark.Age += TickSeconds;
            if (spark.Age >= SparkSeconds)
            {
                CelebrationCanvas.Children.Remove(spark.Dot);
                _sparks.RemoveAt(i);
                continue;
            }
            spark.X += spark.Vx * TickSeconds;
            spark.Y += spark.Vy * TickSeconds;
            spark.Vy += 120 * TickSeconds; // 烟花碎屑下坠
            spark.Dot.Opacity = 1 - spark.Age / SparkSeconds;
            Canvas.SetLeft(spark.Dot, spark.X);
            Canvas.SetTop(spark.Dot, spark.Y);
        }

        if (_elapsed >= TotalSeconds) StopCelebration();
    }

    private void SpawnConfetti(int count)
    {
        double width = CanvasWidth();
        for (int i = 0; i < count; i++)
        {
            double w = 6 + Random.Shared.NextDouble() * 6;
            double h = 9 + Random.Shared.NextDouble() * 7;
            var shape = new Rectangle
            {
                Width = w,
                Height = h,
                RadiusX = 2,
                RadiusY = 2,
                Fill = new SolidColorBrush(Palette[Random.Shared.Next(Palette.Length)])
            };
            var spin = new RotateTransform(0, w / 2, h / 2);
            shape.RenderTransform = spin;

            var flake = new Flake
            {
                Shape = shape,
                Spin = spin,
                BaseX = Random.Shared.NextDouble() * width,
                // 从窗顶上方 30~320 像素开始落，让纸屑陆续进场而不是同时出现
                Y = -(30 + Random.Shared.NextDouble() * 290),
                Fall = 150 + Random.Shared.NextDouble() * 170,
                Sway = 1.2 + Random.Shared.NextDouble() * 1.6,
                Phase = Random.Shared.NextDouble() * Math.PI * 2,
                SpinSpeed = (Random.Shared.NextDouble() < 0.5 ? -1 : 1) * (90 + Random.Shared.NextDouble() * 240)
            };
            Canvas.SetLeft(shape, flake.BaseX);
            Canvas.SetTop(shape, flake.Y);
            CelebrationCanvas.Children.Add(shape);
            _flakes.Add(flake);
        }
    }

    private void SpawnFirework()
    {
        double width = CanvasWidth();
        double height = CanvasHeight();
        double centerX = width * (0.15 + Random.Shared.NextDouble() * 0.7);
        double centerY = height * (0.1 + Random.Shared.NextDouble() * 0.45);
        Color color = Palette[Random.Shared.Next(Palette.Length)];

        for (int i = 0; i < SparksPerFirework; i++)
        {
            double angle = i * (2 * Math.PI / SparksPerFirework) + Random.Shared.NextDouble() * 0.15;
            double speed = 70 + Random.Shared.NextDouble() * 90;
            var dot = new Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = new SolidColorBrush(i % 3 == 0 ? Colors.White : color)
            };
            CelebrationCanvas.Children.Add(dot);
            Canvas.SetLeft(dot, centerX);
            Canvas.SetTop(dot, centerY);
            _sparks.Add(new Spark
            {
                Dot = dot,
                X = centerX,
                Y = centerY,
                Vx = Math.Cos(angle) * speed,
                Vy = Math.Sin(angle) * speed
            });
        }
    }

    /// <summary>覆盖层按窗口客户区铺开；尚未完成布局时退回窗体标称尺寸，避免除零或全 0。</summary>
    private double CanvasWidth() => CelebrationCanvas.ActualWidth > 0 ? CelebrationCanvas.ActualWidth : 520;

    private double CanvasHeight() => CelebrationCanvas.ActualHeight > 0 ? CelebrationCanvas.ActualHeight : 320;

    private sealed class Flake
    {
        public required Rectangle Shape { get; init; }
        public required RotateTransform Spin { get; init; }
        public double BaseX { get; init; }
        public double Y { get; set; }
        public double Fall { get; init; }
        public double Sway { get; init; }
        public double Phase { get; init; }
        public double SpinSpeed { get; init; }
    }

    private sealed class Spark
    {
        public required Ellipse Dot { get; init; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Vx { get; init; }
        public double Vy { get; set; }
        public double Age { get; set; }
    }
}
