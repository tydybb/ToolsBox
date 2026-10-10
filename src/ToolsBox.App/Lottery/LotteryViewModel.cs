using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using ToolsBox.App.Infrastructure;
using ToolsBox.Core.Lottery;

namespace ToolsBox.App.Lottery;

public sealed partial class LotteryViewModel : ObservableObject, IDisposable
{
    private readonly LotteryStore _store;
    private readonly ILotteryClient _client;
    private readonly Func<string, bool> _confirm;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<string, string?> _requestFavoriteName;
    private readonly CancellationTokenSource _lifetime = new();
    private LotteryState _state = new();
    private bool _busy, _readOnly, _scheduleConfirmed, _realBuy;
    private string _status = "进入页面后获取最新开奖，也可手动刷新。", _blueInput = "", _count = "5", _multiple = "1";
    public IReadOnlyList<LotteryNumberInput> RedInputs { get; } = Enumerable.Range(0, 6).Select(_ => new LotteryNumberInput()).ToArray();
    private LotteryNumbers? _selectedDraft;
    private LotteryFavorite? _selectedFavorite;
    public ObservableCollection<LotteryNumbers> Draft { get; } = new();
    public ObservableCollection<LotteryFavorite> Favorites { get; } = new();
    public ObservableCollection<LotteryDrawRow> Draws { get; } = new();
    public ObservableCollection<LotteryRecordRow> Records { get; } = new();

    public LotteryViewModel(LotteryStore? store = null, ILotteryClient? client = null,
        Func<string, bool>? confirm = null, Func<DateTimeOffset>? now = null, Func<string, string?>? requestFavoriteName = null)
    {
        _store = store ?? new LotteryStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolsBox", "lottery.json"));
        _client = client ?? new CwlLotteryClient();
        _confirm = confirm ?? (message => ComfortMessageBox.Show(message, "双色球记账确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
        _now = now ?? (() => DateTimeOffset.Now);
        _requestFavoriteName = requestFavoriteName ?? LotteryFavoriteNameDialog.Prompt;
        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(), () => CanEdit);
        FullHistoryCommand = new AsyncRelayCommand(() => RefreshAsync(true), () => CanEdit);
        BuyCommand = new AsyncRelayCommand(BuyAsync, () => CanEdit && Draft.Count > 0 && ScheduleConfirmed);
        GenerateCommand = new RelayCommand(() => Edit(() =>
        {
            int count = Integer(CountInput, "机选注数");
            LotteryRules.BatchCost(Draft.Count + count, Integer(MultipleInput, "倍数"));
            foreach (var numbers in LotteryNumbers.MachinePick(count)) Draft.Add(numbers);
            Status = $"已追加 {count} 注机选号码。";
        }));
        AddManualCommand = new RelayCommand(() => Edit(() =>
        {
            var numbers = LotteryNumbers.Parse(RedInput + " + " + BlueInput);
            LotteryRules.BatchCost(Draft.Count + 1, Integer(MultipleInput, "倍数"));
            Draft.Add(numbers); SelectedDraft = numbers; Status = "已加入自选号码。";
        }));
        RemoveDraftCommand = new RelayCommand(() => Edit(() => { if (SelectedDraft is { } n) Draft.Remove(n); }));
        ClearDraftCommand = new RelayCommand(() => Edit(Draft.Clear));
        SaveFavoriteCommand = new RelayCommand(() => Edit(() =>
        {
            var numbers = LotteryNumbers.Parse(RedInput + " + " + BlueInput);
            if (_state.Favorites.Any(x => x.Numbers.ToString() == numbers.ToString())) throw new ArgumentException("该手动号码已保存。");
            string? requestedName = _requestFavoriteName($"幸运号码 {_state.Favorites.Count + 1}");
            if (requestedName is null) return;
            string name = requestedName.Trim();
            if (name.Length is < 1 or > 40) throw new ArgumentException("幸运号码名称须为 1–40 个字符。");
            if (_state.Favorites.Any(x => x.Name == name || x.Numbers.ToString() == numbers.ToString())) throw new ArgumentException("该名称或号码已保存。");
            Save(NewState(favorites: [.. _state.Favorites, new(name, numbers)])); Status = "幸运号码已保存。";
        }));
        UseFavoriteCommand = new RelayCommand(() => Edit(() =>
        {
            if (SelectedFavorite is not { } favorite) throw new ArgumentException("请先选择幸运号码。");
            LotteryRules.BatchCost(Draft.Count + 1, Integer(MultipleInput, "倍数"));
            Draft.Add(favorite.Numbers); Status = "幸运号码已加入选号列表，请到选号买入页确认。";
        }));
        DeleteFavoriteCommand = new RelayCommand(() => Edit(() =>
        {
            if (SelectedFavorite is { } favorite && _confirm($"删除幸运号码“{favorite.Name}”？"))
                Save(NewState(favorites: _state.Favorites.Where(x => x != favorite).ToList()));
        }));
        Draft.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(DraftSummary)); BuyCommand.RaiseCanExecuteChanged(); };
        try { _state = _store.Load(); UpdateRows(); }
        catch (Exception ex) { _readOnly = true; Status = "本地账本读取失败，已禁止覆盖原文件：" + ex.Message; }
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand FullHistoryCommand { get; }
    public AsyncRelayCommand BuyCommand { get; }
    public RelayCommand GenerateCommand { get; }
    public RelayCommand AddManualCommand { get; }
    public RelayCommand RemoveDraftCommand { get; }
    public RelayCommand ClearDraftCommand { get; }
    public RelayCommand SaveFavoriteCommand { get; }
    public RelayCommand UseFavoriteCommand { get; }
    public RelayCommand DeleteFavoriteCommand { get; }
    public bool CanEdit => !_busy && !_readOnly && !_lifetime.IsCancellationRequested;
    public bool IsBusy => _busy;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string RedInput
    {
        get => string.Join(" ", RedInputs.Select(x => x.Text));
        set
        {
            var parts = value.Split(new[] { ' ', ',', '，', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 6) throw new ArgumentException("红球只能输入 6 个。");
            for (int i = 0; i < 6; i++) RedInputs[i].Text = i < parts.Length ? parts[i] : "";
            OnPropertyChanged();
        }
    }
    public string BlueInput { get => _blueInput; set => SetProperty(ref _blueInput, value); }
    public string CountInput { get => _count; set => SetProperty(ref _count, value); }
    public string MultipleInput { get => _multiple; set { SetProperty(ref _multiple, value); OnPropertyChanged(nameof(DraftSummary)); } }
    public LotteryNumbers? SelectedDraft { get => _selectedDraft; set => SetProperty(ref _selectedDraft, value); }
    public LotteryFavorite? SelectedFavorite { get => _selectedFavorite; set => SetProperty(ref _selectedFavorite, value); }
    public bool IsRealBuy { get => _realBuy; set => SetProperty(ref _realBuy, value); }
    public bool ScheduleConfirmed { get => _scheduleConfirmed; set { SetProperty(ref _scheduleConfirmed, value); BuyCommand.RaiseCanExecuteChanged(); } }
    public string NextIssue { get; private set; } = "等待联网确认";
    public string NextDate { get; private set; } = "—";
    public string SourceStatus { get; private set; } = "来源：中国福彩网，尚无缓存";
    public string RealSummary { get; private set; } = "";
    public string FakeSummary { get; private set; } = "";
    public decimal TotalNet { get; private set; }
    public string TotalSummary { get; private set; } = "";
    public string NextDrawStatus
    {
        get
        {
            var latest = _state.Draws.MaxBy(x => x.Issue, StringComparer.Ordinal);
            if (latest is null) return "下次开奖时间：待获取最新开奖数据";
            var date = LotteryRules.NextRegularDrawDate(latest.DrawDate);
            var time = new DateTimeOffset(date.ToDateTime(new TimeOnly(21, 15)), TimeSpan.FromHours(8));
            if (date.Year != latest.DrawDate.Year || _now() >= time)
                return "下次开奖时间：待更新确认（缓存期次已过预计时间或跨年，请刷新）";
            return $"下次预计开奖：{date:yyyy-MM-dd} 21:15（北京时间） · 第 {NextIssue} 期 · 休市调整以官方公告为准";
        }
    }
    public string DraftSummary
    {
        get
        {
            if (Draft.Count == 0) return "尚未选号";
            try { return $"{Draft.Count} 注 × {Integer(MultipleInput, "倍数")} 倍 × 2 元 = {LotteryRules.BatchCost(Draft.Count, Integer(MultipleInput, "倍数")):N2} 元"; }
            catch (ArgumentException) { return "请输入 1–99 的整数倍数，单批不超过 20000 元"; }
        }
    }
    private void Edit(Action action)
    {
        if (!CanEdit) return;
        try { action(); } catch (Exception ex) { Status = ex.Message; }
    }
    private static int Integer(string text, string name) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0
        ? n : throw new ArgumentException(name + "必须为正整数。");
    private void Busy(bool value)
    {
        _busy = value; OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(CanEdit));
        RefreshCommand.RaiseCanExecuteChanged(); FullHistoryCommand.RaiseCanExecuteChanged(); BuyCommand.RaiseCanExecuteChanged();
    }
    public void Dispose() { _lifetime.Cancel(); }
}
