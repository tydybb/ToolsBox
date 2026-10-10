using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ToolsBox.App.WorkCountdown;

public sealed class CountdownSticker : Image
{
    public static readonly DependencyProperty AnimationKeyProperty = DependencyProperty.Register(
        nameof(AnimationKey), typeof(string), typeof(CountdownSticker),
        new PropertyMetadata(string.Empty, OnAnimationKeyChanged));

    public static readonly DependencyProperty AnimationEnabledProperty = DependencyProperty.Register(
        nameof(AnimationEnabled), typeof(bool), typeof(CountdownSticker),
        new PropertyMetadata(true, OnPlaybackSettingChanged));

    private readonly DispatcherTimer _timer;
    private IReadOnlyList<BitmapSource> _frames = [];
    private string? _currentKey;
    private Window? _hostWindow;

    public CountdownSticker()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(160)
        };
        _timer.Tick += OnTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnVisibilityChanged;
    }

    public string AnimationKey
    {
        get => (string?)GetValue(AnimationKeyProperty) ?? string.Empty;
        set => SetValue(AnimationKeyProperty, value);
    }

    public bool AnimationEnabled
    {
        get => (bool)GetValue(AnimationEnabledProperty);
        set => SetValue(AnimationEnabledProperty, value);
    }

    public bool IsPlaying => _timer.IsEnabled;
    public int CurrentFrameIndex { get; private set; }

    private static void OnAnimationKeyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((CountdownSticker)sender).ChangeAnimation((string?)args.NewValue);

    private static void OnPlaybackSettingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((CountdownSticker)sender).UpdatePlayback();

    private void ChangeAnimation(string? key)
    {
        if (string.Equals(_currentKey, key, StringComparison.Ordinal)) return;
        _timer.Stop();
        _currentKey = key;
        _frames = CountdownSpriteCatalog.GetFrames(key);
        CurrentFrameIndex = 0;
        Source = _frames.Count == 0 ? null : _frames[0];
        UpdatePlayback();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        AttachHost(Window.GetWindow(this));
        UpdatePlayback();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _timer.Stop();
        AttachHost(null);
    }

    private void AttachHost(Window? window)
    {
        if (ReferenceEquals(_hostWindow, window)) return;
        if (_hostWindow is not null) _hostWindow.StateChanged -= OnHostStateChanged;
        _hostWindow = window;
        if (_hostWindow is not null) _hostWindow.StateChanged += OnHostStateChanged;
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) => UpdatePlayback();
    private void OnHostStateChanged(object? sender, EventArgs args) => UpdatePlayback();

    private bool CanPlay => IsLoaded && IsVisible && AnimationEnabled && _frames.Count > 1
        && _hostWindow is not null && _hostWindow.WindowState != WindowState.Minimized;

    private void UpdatePlayback()
    {
        if (CanPlay)
        {
            if (!_timer.IsEnabled) _timer.Start();
        }
        else _timer.Stop();
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (!CanPlay)
        {
            _timer.Stop();
            return;
        }
        CurrentFrameIndex = (CurrentFrameIndex + 1) % _frames.Count;
        Source = _frames[CurrentFrameIndex];
    }
}
