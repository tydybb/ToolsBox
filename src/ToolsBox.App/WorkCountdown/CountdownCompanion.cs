using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.App.WorkCountdown;

/// <summary>A bounded walking sticker whose name always travels with the character.</summary>
public sealed class CountdownCompanion : Grid
{
    public static readonly DependencyProperty AnimationKeyProperty = DependencyProperty.Register(
        nameof(AnimationKey), typeof(string), typeof(CountdownCompanion),
        new PropertyMetadata(string.Empty, OnAnimationKeyChanged));

    public static readonly DependencyProperty CharacterNameProperty = DependencyProperty.Register(
        nameof(CharacterName), typeof(string), typeof(CountdownCompanion),
        new PropertyMetadata(string.Empty, OnCharacterNameChanged));

    public static readonly DependencyProperty SpeechTextProperty = DependencyProperty.Register(
        nameof(SpeechText), typeof(string), typeof(CountdownCompanion),
        new PropertyMetadata(string.Empty, OnSpeechTextChanged));

    public static readonly DependencyProperty EffectKeyProperty = DependencyProperty.Register(
        nameof(EffectKey), typeof(string), typeof(CountdownCompanion),
        new PropertyMetadata(string.Empty, OnEffectKeyChanged));

    public static readonly DependencyProperty AnimationEnabledProperty = DependencyProperty.Register(
        nameof(AnimationEnabled), typeof(bool), typeof(CountdownCompanion),
        new PropertyMetadata(true, OnPlaybackSettingChanged));

    public static readonly DependencyProperty IsDarkThemeProperty = DependencyProperty.Register(
        nameof(IsDarkTheme), typeof(bool), typeof(CountdownCompanion),
        new PropertyMetadata(false, OnThemeChanged));

    public static readonly DependencyProperty CharacterSizeProperty = DependencyProperty.Register(
        nameof(CharacterSize), typeof(double), typeof(CountdownCompanion),
        new PropertyMetadata(128d, OnCharacterSizeChanged),
        value => value is double size && double.IsFinite(size) && size is >= 48 and <= 256);

    private readonly CountdownFreeRoamMotion _motion = new();
    private readonly Func<bool> _animationPreference;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _interactionTimer;
    private readonly DispatcherTimer _effectTimer;
    private readonly Stopwatch _elapsed = new();
    private readonly Stopwatch _effectElapsed = new();
    private readonly List<Particle> _particles = new(24);
    private readonly TranslateTransform _translation = new();
    private readonly ScaleTransform _imageDirection = new(1, 1);
    private Window? _hostWindow;
    private TimeSpan _lastElapsed;
    private string _walkingKey = string.Empty;
    private bool _hasWalkingFrames;
    private bool _isHovered;
    private bool _hoverFacingRight = true;
    private bool _interactionActive;
    private bool _effectPending;
    private int _interactionIndex;
    private int _activeInteractionIndex;
    private double _effectDuration;
    private double _speechWidth;
    private double _speechHeight;
    private double _speechNaturalHeight;
    public CountdownCompanion() : this(() => SystemParameters.ClientAreaAnimation) { }

    internal CountdownCompanion(Func<bool> animationPreference)
    {
        _animationPreference = animationPreference;
        ClipToBounds = true;
        MinHeight = CharacterSize + 22;
        Sticker = new CountdownSticker
        {
            Width = CharacterSize,
            Height = CharacterSize,
            Stretch = Stretch.Uniform,
            RenderTransform = _imageDirection,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Focusable = true,
            Cursor = Cursors.Hand
        };
        RenderOptions.SetBitmapScalingMode(Sticker, BitmapScalingMode.HighQuality);
        NameText = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
            IsHitTestVisible = false
        };
        NameText.SetResourceReference(TextBlock.ForegroundProperty, "ToolboxTextBrush");
        var group = new StackPanel { Width = CharacterSize, RenderTransform = _translation };
        group.Children.Add(Sticker);
        group.Children.Add(NameText);
        MotionGroup = group;
        EffectLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        Children.Add(EffectLayer);
        var area = new Canvas();
        area.Children.Add(group);
        Children.Add(area);
        SpeechBubbleText = new TextBlock
        {
            FontSize = 12,
            LineHeight = 16,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center
        };
        SpeechBubbleText.SetResourceReference(TextBlock.ForegroundProperty, "ToolboxTextBrush");
        SpeechBubble = new Border
        {
            Child = SpeechBubbleText,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 5, 9, 5),
            BorderThickness = new Thickness(1),
            MaxHeight = 46,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        SpeechBubble.SetResourceReference(Border.BackgroundProperty, "ToolboxInfoSurfaceBrush");
        SpeechBubble.SetResourceReference(Border.BorderBrushProperty, "ToolboxBorderBrush");
        var speechLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        speechLayer.Children.Add(SpeechBubble);
        Children.Add(speechLayer);

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _timer.Tick += OnTick;
        _interactionTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(2600)
        };
        _interactionTimer.Tick += OnInteractionExpired;
        _effectTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _effectTimer.Tick += OnEffectTick;
        Sticker.MouseEnter += OnCharacterMouseEnter;
        Sticker.MouseLeave += OnCharacterMouseLeave;
        Sticker.MouseMove += OnCharacterMouseMove;
        Sticker.MouseLeftButtonUp += OnCharacterClick;
        Sticker.KeyDown += OnCharacterKeyDown;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnVisibilityChanged;
        SizeChanged += OnSizeChanged;
        MotionGroup.SizeChanged += OnSizeChanged;
        Sticker.AnimationEnabled = false;
        UpdateCharacterName();
    }

    public string AnimationKey
    {
        get => (string?)GetValue(AnimationKeyProperty) ?? string.Empty;
        set => SetValue(AnimationKeyProperty, value);
    }

    public string CharacterName
    {
        get => (string?)GetValue(CharacterNameProperty) ?? string.Empty;
        set => SetValue(CharacterNameProperty, value);
    }

    public string SpeechText
    {
        get => (string?)GetValue(SpeechTextProperty) ?? string.Empty;
        set => SetValue(SpeechTextProperty, value);
    }

    public string EffectKey
    {
        get => (string?)GetValue(EffectKeyProperty) ?? string.Empty;
        set => SetValue(EffectKeyProperty, value);
    }

    public bool AnimationEnabled
    {
        get => (bool)GetValue(AnimationEnabledProperty);
        set => SetValue(AnimationEnabledProperty, value);
    }

    public bool IsDarkTheme
    {
        get => (bool)GetValue(IsDarkThemeProperty);
        set => SetValue(IsDarkThemeProperty, value);
    }

    public double CharacterSize
    {
        get => (double)GetValue(CharacterSizeProperty);
        set => SetValue(CharacterSizeProperty, value);
    }

    public CountdownSticker Sticker { get; }
    public TextBlock NameText { get; }
    public FrameworkElement MotionGroup { get; }
    public Border SpeechBubble { get; }
    public TextBlock SpeechBubbleText { get; }
    public Canvas EffectLayer { get; }
    public bool IsWalking => IsMotionRunning && _motion.IsWalking;
    public bool IsMotionRunning => _timer.IsEnabled;
    public double Position => _motion.Position;
    public double VerticalPosition => _motion.VerticalPosition;

    private static void OnAnimationKeyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((CountdownCompanion)sender).ChangeAnimationKey();

    private static void OnCharacterNameChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var companion = (CountdownCompanion)sender;
        companion._activeInteractionIndex = 0;
        companion._interactionIndex = companion._interactionActive ? 1 : 0;
        companion.UpdateCharacterName();
        companion.UpdateSpeech();
    }

    private static void OnSpeechTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((CountdownCompanion)sender).UpdateSpeech();

    private static void OnEffectKeyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var companion = (CountdownCompanion)sender;
        companion._effectPending = true;
        companion.UpdateSpeech();
        companion.UpdatePlayback();
    }

    private static void OnPlaybackSettingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((CountdownCompanion)sender).UpdatePlayback();

    private static void OnThemeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((CountdownCompanion)sender).UpdateTheme();

    private static void OnCharacterSizeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((CountdownCompanion)sender).ResizeCharacter();

    private void ResizeCharacter()
    {
        Sticker.Width = CharacterSize;
        Sticker.Height = CharacterSize;
        MotionGroup.Width = CharacterSize;
        MinHeight = CharacterSize + 22;
        ResizeBounds();
    }

    private void ChangeAnimationKey()
    {
        int separator = AnimationKey.IndexOf('/');
        _walkingKey = separator > 0 && separator == AnimationKey.LastIndexOf('/')
            ? AnimationKey[..(separator + 1)] + "walk-" + AnimationKey[(separator + 1)..]
            : string.Empty;
        _hasWalkingFrames = CountdownSpriteCatalog.GetFrames(_walkingKey).Count > 1;
        UpdatePlayback();
    }

    private void UpdateTheme()
    {
        // Keep the alpha background untouched; a tiny glow makes black ink legible on a dark card.
        Sticker.Effect = IsDarkTheme ? new DropShadowEffect
        {
            Color = Colors.White,
            ShadowDepth = 0,
            BlurRadius = 2,
            Opacity = 0.3
        } : null;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        AttachHost(Window.GetWindow(this));
        SystemParameters.StaticPropertyChanged += OnSystemAnimationPreferenceChanged;
        ResizeBounds();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        StopMotion();
        Sticker.AnimationEnabled = false;
        CancelInteraction();
        StopEffects();
        _effectPending = false;
        SystemParameters.StaticPropertyChanged -= OnSystemAnimationPreferenceChanged;
        AttachHost(null);
        UpdateVisuals();
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
    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => ResizeBounds();

    private void OnSystemAnimationPreferenceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SystemParameters.ClientAreaAnimation)) return;
        if (Dispatcher.CheckAccess()) UpdatePlayback();
        else Dispatcher.InvokeAsync(UpdatePlayback);
    }

    private void ResizeBounds()
    {
        // Wait for the name and image to be measured before choosing the first centered position.
        // A new character size takes effect immediately, before the next layout pass updates ActualSize.
        double groupWidth = MotionGroup.ActualWidth > 0 ? CharacterSize : double.NaN;
        double groupHeight = MotionGroup.ActualHeight > 0
            ? CharacterSize + NameText.ActualHeight + NameText.Margin.Top + NameText.Margin.Bottom
            : double.NaN;
        _motion.Resize(ActualWidth, ActualHeight, groupWidth, groupHeight);
        ResizeSpeechBubble();
        PositionParticles(_effectElapsed.Elapsed.TotalSeconds);
        UpdatePlayback();
    }

    private bool CanDisplay => IsLoaded && IsVisible && _hostWindow is not null
        && _hostWindow.WindowState != WindowState.Minimized;

    private bool CanPlay => CanDisplay && AnimationEnabled && _animationPreference();

    private bool CanMove => CanPlay && _hasWalkingFrames && _motion.CanMove && !_isHovered && !_interactionActive;

    private void UpdatePlayback()
    {
        if (!CanDisplay) CancelInteraction();
        if (!CanPlay) StopEffects();
        if (CanMove)
        {
            if (!_timer.IsEnabled)
            {
                _lastElapsed = TimeSpan.Zero;
                _elapsed.Restart();
                _timer.Start();
            }
        }
        else StopMotion();
        Sticker.AnimationEnabled = CanPlay;
        UpdateVisuals();
        if (_effectPending && IsLoaded)
        {
            _effectPending = false;
            StartEffects(EffectKey);
        }
    }

    private void StopMotion()
    {
        _timer.Stop();
        _elapsed.Reset();
        _lastElapsed = TimeSpan.Zero;
    }

    private void UpdateVisuals()
    {
        _translation.X = _motion.Position;
        _translation.Y = _motion.VerticalPosition;
        _imageDirection.ScaleX = (_isHovered ? _hoverFacingRight : _motion.FacingRight) ? 1 : -1;
        Sticker.AnimationKey = _interactionActive ? HappyAnimationKey() : IsWalking ? _walkingKey : AnimationKey;
        PositionSpeechBubble();
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (!CanMove)
        {
            UpdatePlayback();
            return;
        }
        TimeSpan elapsed = _elapsed.Elapsed;
        _motion.Advance(elapsed - _lastElapsed);
        _lastElapsed = elapsed;
        UpdateVisuals();
    }

    private void UpdateCharacterName()
    {
        NameText.Text = CharacterName;
        AutomationProperties.SetName(Sticker, CharacterName + "，按回车或空格和我互动");
    }

    private void OnCharacterMouseEnter(object sender, MouseEventArgs args)
    {
        _isHovered = true;
        FacePointer(args);
        UpdateSpeech();
        UpdatePlayback();
    }

    private void OnCharacterMouseLeave(object sender, MouseEventArgs args)
    {
        _isHovered = false;
        UpdateSpeech();
        UpdatePlayback();
    }

    private void OnCharacterMouseMove(object sender, MouseEventArgs args)
    {
        if (!_isHovered) return;
        FacePointer(args);
        UpdateVisuals();
    }

    private void FacePointer(MouseEventArgs args) =>
        _hoverFacingRight = args.GetPosition(this).X >= Position + CharacterSize / 2;

    private void OnCharacterClick(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left) return;
        Sticker.Focus();
        Interact();
        args.Handled = true;
    }

    private void OnCharacterKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key is not (Key.Enter or Key.Space) || args.IsRepeat) return;
        Interact();
        args.Handled = true;
    }

    private void Interact()
    {
        if (!CanDisplay) return;
        _activeInteractionIndex = _interactionIndex;
        _interactionIndex = (_interactionIndex + 1) % CountdownCharacterDialogue.InteractionCount;
        _interactionActive = true;
        _interactionTimer.Stop();
        _interactionTimer.Start();
        UpdateSpeech();
        UpdatePlayback();
        StartEffects("celebrate", 10);
    }

    private void OnInteractionExpired(object? sender, EventArgs args)
    {
        _interactionTimer.Stop();
        _interactionActive = false;
        UpdateSpeech();
        UpdatePlayback();
    }

    private void CancelInteraction()
    {
        _interactionTimer.Stop();
        _interactionActive = false;
        _isHovered = false;
        UpdateSpeech();
    }

    private string HappyAnimationKey()
    {
        int separator = AnimationKey.IndexOf('/');
        string happyKey = separator > 0 ? AnimationKey[..(separator + 1)] + "happy" : string.Empty;
        return CountdownSpriteCatalog.GetFrames(happyKey).Count > 0 ? happyKey : AnimationKey;
    }

    private void UpdateSpeech()
    {
        SpeechBubbleText.Text = _interactionActive
            ? CountdownCharacterDialogue.Click(CharacterName, _activeInteractionIndex)
            : _isHovered ? CountdownCharacterDialogue.Hover(CharacterName, EffectKey) : SpeechText;
        SpeechBubble.Visibility = string.IsNullOrWhiteSpace(SpeechBubbleText.Text) ? Visibility.Collapsed : Visibility.Visible;
        ResizeSpeechBubble();
    }

    private void ResizeSpeechBubble()
    {
        if (SpeechBubble.Visibility != Visibility.Visible || ActualWidth <= 8 || ActualHeight <= 8) return;
        MeasureSpeechBubble(Math.Min(184, ActualWidth - 8), false);
        _speechNaturalHeight = _speechHeight;
        PositionSpeechBubble();
    }

    private void PositionSpeechBubble()
    {
        if (SpeechBubble.Visibility != Visibility.Visible) return;
        const double gap = 4;
        double groupHeight = MotionGroup.ActualHeight > 0 ? MotionGroup.ActualHeight : CharacterSize + 22;
        double above = Math.Max(0, VerticalPosition - gap);
        double below = Math.Max(0, ActualHeight - VerticalPosition - groupHeight - gap);
        double width = Math.Max(1, Math.Min(184, ActualWidth - 8));
        bool compact = Math.Max(above, below) < _speechNaturalHeight;
        double x, y;
        if (compact && Math.Max(above, below) < 24)
        {
            double left = Math.Max(0, Position - gap);
            double right = Math.Max(0, ActualWidth - Position - CharacterSize - gap);
            if (Math.Max(left, right) < 22)
            {
                SpeechBubble.Opacity = 0;
                return;
            }
            MeasureSpeechBubble(Math.Min(184, Math.Max(left, right)), false);
            x = right >= left ? Position + CharacterSize + gap : Position - gap - _speechWidth;
            y = VerticalPosition + CharacterSize / 2 - _speechHeight / 2;
        }
        else
        {
            MeasureSpeechBubble(width, compact);
            x = Position + CharacterSize / 2 - _speechWidth / 2;
            y = above >= _speechHeight ? VerticalPosition - _speechHeight - gap : VerticalPosition + groupHeight + gap;
        }
        SpeechBubble.Opacity = 1;
        Canvas.SetLeft(SpeechBubble, Math.Clamp(x, 0, Math.Max(0, ActualWidth - _speechWidth)));
        Canvas.SetTop(SpeechBubble, Math.Clamp(y, 0, Math.Max(0, ActualHeight - _speechHeight)));
    }

    private void MeasureSpeechBubble(double width, bool compact)
    {
        SpeechBubbleText.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap;
        SpeechBubble.Padding = compact ? new Thickness(8, 3, 8, 3) : new Thickness(9, 5, 9, 5);
        SpeechBubble.MaxWidth = width;
        SpeechBubble.MaxHeight = Math.Max(1, Math.Min(compact ? 24 : 46, ActualHeight - 8));
        SpeechBubble.Measure(new Size(SpeechBubble.MaxWidth, SpeechBubble.MaxHeight));
        _speechWidth = SpeechBubble.DesiredSize.Width;
        _speechHeight = SpeechBubble.DesiredSize.Height;
    }

    private void StartEffects(string key, int? maximumCount = null)
    {
        StopEffects();
        if (!CanPlay || ActualWidth <= 0 || ActualHeight <= 0) return;
        int count = key switch
        {
            "rest" => 7, "focus" => 10, "rush" => 14,
            "overtime" => 10, "overdue" => 12, "celebrate" => 24,
            _ => 0
        };
        if (maximumCount is not null) count = Math.Min(count, maximumCount.Value);
        if (count == 0) return;
        _effectDuration = key == "celebrate" ? 1.8 : 1.5;
        double centerX = Position + CharacterSize / 2;
        double centerY = VerticalPosition + CharacterSize * 0.35;
        for (int index = 0; index < count; index++)
        {
            var particle = new TextBlock
            {
                Text = key switch
                {
                    "rest" => "z", "rush" => "›", "overtime" => "·", "overdue" => "!",
                    "celebrate" => index % 2 == 0 ? "✦" : "·", _ => "✧"
                },
                Width = Math.Min(14, ActualWidth),
                Height = Math.Min(16, ActualHeight),
                FontSize = key is "rush" or "overtime" ? 16 : 12,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
            };
            particle.SetResourceReference(TextBlock.ForegroundProperty, key switch
            {
                "rest" => "ToolboxMutedBrush",
                "rush" or "overtime" => "ToolboxWarningTextBrush",
                "overdue" => "ToolboxDangerTextBrush",
                "celebrate" when index % 3 == 0 => "ToolboxSuccessTextBrush",
                "celebrate" when index % 3 == 1 => "ToolboxWarningTextBrush",
                _ => "ToolboxAccentBrush"
            });
            double horizontal = Random.Shared.NextDouble() * 2 - 1;
            double vertical = Random.Shared.NextDouble();
            _particles.Add(new Particle(particle,
                centerX + horizontal * CharacterSize * 0.3,
                centerY + (vertical - 0.5) * CharacterSize * 0.3,
                key == "rush" ? (_motion.FacingRight ? 48 : -48) : horizontal * (key == "rest" ? 8 : 36),
                key == "overdue" ? 12 + vertical * 16 : -12 - vertical * (key == "celebrate" ? 36 : 18),
                key is "celebrate" or "overdue" ? 22 : 0));
            EffectLayer.Children.Add(particle);
        }
        PositionParticles(0);
        _effectElapsed.Restart();
        _effectTimer.Start();
    }

    private void OnEffectTick(object? sender, EventArgs args)
    {
        double seconds = _effectElapsed.Elapsed.TotalSeconds;
        if (!CanPlay || seconds >= _effectDuration)
        {
            StopEffects();
            return;
        }
        PositionParticles(seconds);
    }

    private void PositionParticles(double seconds)
    {
        foreach (Particle particle in _particles)
        {
            particle.Visual.Width = Math.Min(14, ActualWidth);
            particle.Visual.Height = Math.Min(16, ActualHeight);
            double x = particle.X + particle.VelocityX * seconds;
            double y = particle.Y + particle.VelocityY * seconds + particle.Gravity * seconds * seconds / 2;
            Canvas.SetLeft(particle.Visual, Math.Clamp(x, 0, Math.Max(0, ActualWidth - particle.Visual.Width)));
            Canvas.SetTop(particle.Visual, Math.Clamp(y, 0, Math.Max(0, ActualHeight - particle.Visual.Height)));
            particle.Visual.Opacity = Math.Clamp(0.8 * (1 - seconds / _effectDuration), 0, 0.8);
        }
    }

    private void StopEffects()
    {
        _effectTimer.Stop();
        _effectElapsed.Reset();
        _particles.Clear();
        EffectLayer.Children.Clear();
    }

    private sealed record Particle(TextBlock Visual, double X, double Y, double VelocityX, double VelocityY, double Gravity);
}
