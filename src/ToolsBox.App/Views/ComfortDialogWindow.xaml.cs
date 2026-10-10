using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ToolsBox.App.Infrastructure;

namespace ToolsBox.App.Views;

/// <summary>工具箱自有提示窗：统一外观，但保留明确的确认结果与安全的关闭语义。</summary>
public partial class ComfortDialogWindow : Window
{
    private readonly bool _dark;
    private readonly Button _defaultButton;
    private ImageSource? _artwork;

    public ComfortDialogWindow(string message, string caption = "宝哥工具箱",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None, Window? owner = null)
    {
        MessageBoxResult[] choices = buttons switch
        {
            MessageBoxButton.OK => [MessageBoxResult.OK],
            MessageBoxButton.OKCancel => [MessageBoxResult.OK, MessageBoxResult.Cancel],
            MessageBoxButton.YesNo => [MessageBoxResult.Yes, MessageBoxResult.No],
            MessageBoxButton.YesNoCancel => [MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel],
            _ => throw new ArgumentOutOfRangeException(nameof(buttons))
        };
        if (!Enum.IsDefined(image)) throw new ArgumentOutOfRangeException(nameof(image));
        if (defaultResult != MessageBoxResult.None && !choices.Contains(defaultResult))
            throw new ArgumentException("默认结果必须对应本提示窗中的按钮。", nameof(defaultResult));

        InitializeComponent();
        Title = caption;
        DialogHeading.Text = caption;
        DialogMessage.Text = message;
        AutomationProperties.SetName(this, caption);
        AutomationProperties.SetName(DialogMessage, message);
        // 单按钮仅为告知；所有含确认的窗口，标题栏关闭或 Escape 始终拒绝操作。
        Result = buttons == MessageBoxButton.OK ? MessageBoxResult.OK
            : choices.Contains(MessageBoxResult.Cancel) ? MessageBoxResult.Cancel : MessageBoxResult.No;
        MessageBoxResult initial = defaultResult == MessageBoxResult.None ? Result : defaultResult;
        _dark = owner?.TryFindResource("ToolboxIsDarkTheme") is bool dark ? dark : ComfortAppearance.LoadDarkPreference();
        ComfortAppearance.Apply(this, _dark);
        if (owner is { IsVisible: true })
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Topmost = owner.Topmost;
        }
        ConfigureIcon(image);
        foreach (MessageBoxResult choice in choices)
        {
            var button = new Button
            {
                Content = choice switch { MessageBoxResult.OK => "确定", MessageBoxResult.Cancel => "取消", MessageBoxResult.Yes => "是", _ => "否" },
                Tag = choice,
                MinWidth = 88,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = choice == initial,
                Style = (Style)FindResource(choice is MessageBoxResult.OK or MessageBoxResult.Yes ? "ToolboxPrimaryButtonStyle" : "ToolboxButtonStyle")
            };
            button.Click += OnResultClick;
            ResultButtons.Children.Add(button);
        }
        _defaultButton = ResultButtons.Children.OfType<Button>().Single(button => button.IsDefault);
        ApplyWorkAreaBounds(SystemParameters.WorkArea);
        SourceInitialized += (_, _) =>
        {
            ComfortAppearance.UpdateCaption(this, _dark);
            UpdateMonitorBounds();
        };
        Loaded += (_, _) => _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => Keyboard.Focus(_defaultButton)));
    }

    public MessageBoxResult Result { get; private set; }

    /// <summary>按需在正文右侧展示透明角色图，普通确认窗不占用此空间。</summary>
    public ImageSource? Artwork
    {
        get => _artwork;
        set
        {
            _artwork = value;
            DialogArtwork.Source = value;
            DialogArtwork.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
            ArtworkColumn.Width = new GridLength(value is null ? 0 : 200);
            Width = Math.Min(value is null ? 540 : 700, MaxWidth);
            if (_dark && value is not null)
                DialogArtwork.Effect = new DropShadowEffect { Color = Color.FromRgb(0xEC, 0xF0, 0xF6), BlurRadius = 5, ShadowDepth = 0, Opacity = .18 };
            else DialogArtwork.Effect = null;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private void OnResultClick(object sender, RoutedEventArgs e)
    {
        Result = (MessageBoxResult)((Button)sender).Tag;
        Close();
    }

    private void ConfigureIcon(MessageBoxImage image)
    {
        if (image == MessageBoxImage.None) return;
        string kind = image switch { MessageBoxImage.Error => "Danger", MessageBoxImage.Warning => "Warning", _ => "Info" };
        IconBadge.SetResourceReference(Border.BackgroundProperty, $"Toolbox{kind}SurfaceBrush");
        IconSymbol.SetResourceReference(TextBlock.ForegroundProperty, $"Toolbox{kind}TextBrush");
        IconSymbol.Text = image switch { MessageBoxImage.Error => "×", MessageBoxImage.Warning => "!", MessageBoxImage.Question => "?", _ => "i" };
        IconBadge.Visibility = Visibility.Visible;
    }

    private void UpdateMonitorBounds()
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return;
        var physical = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        Matrix transform = source.CompositionTarget.TransformFromDevice;
        ApplyWorkAreaBounds(new Rect(transform.Transform(new Point(physical.Left, physical.Top)),
            transform.Transform(new Point(physical.Right, physical.Bottom))));
    }

    private void ApplyWorkAreaBounds(Rect area)
    {
        MaxWidth = Math.Max(200, area.Width - 32);
        MaxHeight = Math.Max(180, area.Height - 48);
        MinWidth = Math.Min(360, MaxWidth);
        Width = Math.Min(_artwork is null ? 540 : 700, MaxWidth);
        // 很矮的工作区仍保留完整按钮行；只压缩留白，不压缩可点击按钮或正文的字号。
        bool compact = MaxHeight < 360;
        DialogCard.Margin = new Thickness(compact ? 10 : 16);
        DialogCard.Padding = new Thickness(compact ? 12 : 22);
        IconBadge.Width = IconBadge.Height = compact ? 34 : 42;
        DialogHeading.FontSize = compact ? 18 : 20;
        DialogBody.Margin = new Thickness(0, compact ? 12 : 18, 0, 0);
        DialogFooter.Margin = new Thickness(0, compact ? 12 : 20, 0, 0);
        DialogFooter.Padding = new Thickness(0, compact ? 10 : 16, 0, 0);
        MessageScrollViewer.MaxHeight = Math.Min(420, Math.Max(24, MaxHeight - (compact ? 200 : 250)));
        DialogArtwork.Height = Math.Min(180, MessageScrollViewer.MaxHeight);
    }
}
