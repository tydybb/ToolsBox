using System.IO;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using ToolsBox.App.Infrastructure;
using ToolsBox.Core.ArchiveRecovery;
using ToolsBox.Windows.ArchiveRecovery;

namespace ToolsBox.App.ArchiveRecovery;

public sealed class ArchiveRecoveryViewModel : ObservableObject, IDisposable
{
    private readonly Func<IArchivePasswordVerifier> _createVerifier;
    private CancellationTokenSource? _cancellation;
    private IDisposable? _activeVerifier;
    private bool _isBusy, _disposed, _showPassword;
    private string _archivePath = "", _candidateText = "", _dictionaryPath = "", _prefix = "", _suffix = "", _charset = "0123456789";
    private string _minimumLength = "1", _maximumLength = "4", _timeoutSeconds = "30", _status = "未选择压缩包。", _progress = "尚未开始", _warning = "";
    private string? _matchedPassword;
    private int _mode;

    public ArchiveRecoveryViewModel() : this(() => new ArchiveRecoveryClient(Environment.ProcessPath!)) { }
    public ArchiveRecoveryViewModel(Func<IArchivePasswordVerifier> createVerifier)
    {
        _createVerifier = createVerifier;
        StartCommand = new AsyncRelayCommand(StartAsync, () => CanEdit && !string.IsNullOrWhiteSpace(ArchivePath));
        StopCommand = new RelayCommand(Stop, () => IsBusy && _cancellation is { IsCancellationRequested: false });
        BrowseCommand = new RelayCommand(Browse, () => CanEdit);
        ImportCommand = new RelayCommand(Import, () => CanEdit);
        ClearImportCommand = new RelayCommand(() => DictionaryPath = "", () => CanEdit);
        RevealCommand = new RelayCommand(() => { _showPassword = !_showPassword; OnPropertyChanged(nameof(PasswordDisplay)); }, () => HasResult);
        CopyCommand = new RelayCommand(Copy, () => HasResult);
    }

    public AsyncRelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ClearImportCommand { get; }
    public RelayCommand RevealCommand { get; }
    public RelayCommand CopyCommand { get; }
    public string ArchivePath { get => _archivePath; set { if (CanEdit && SetProperty(ref _archivePath, value)) { ClearResult(); Status = File.Exists(value) ? "就绪：开始前会检查格式；不会自动解压。" : "请选择有效的压缩包文件。"; StartCommand.RaiseCanExecuteChanged(); } } }
    public string CandidateText { get => _candidateText; set { if (CanEdit && SetProperty(ref _candidateText, value)) ChangedInput(); } }
    public string DictionaryPath { get => _dictionaryPath; private set { if (SetProperty(ref _dictionaryPath, value)) ChangedInput(); } }
    public string Prefix { get => _prefix; set { if (CanEdit && SetProperty(ref _prefix, value)) ChangedInput(); } }
    public string Suffix { get => _suffix; set { if (CanEdit && SetProperty(ref _suffix, value)) ChangedInput(); } }
    public string Charset { get => _charset; set { if (CanEdit && SetProperty(ref _charset, value)) ChangedInput(); } }
    public string MinimumLength { get => _minimumLength; set { if (CanEdit && SetProperty(ref _minimumLength, value)) ChangedInput(); } }
    public string MaximumLength { get => _maximumLength; set { if (CanEdit && SetProperty(ref _maximumLength, value)) ChangedInput(); } }
    public string TimeoutSeconds { get => _timeoutSeconds; set { if (CanEdit) SetProperty(ref _timeoutSeconds, value); } }
    public int Mode { get => _mode; set { if (CanEdit && SetProperty(ref _mode, value)) ChangedInput(); } }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string ProgressText { get => _progress; private set => SetProperty(ref _progress, value); }
    public string Warning { get => _warning; private set => SetProperty(ref _warning, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) { OnPropertyChanged(nameof(CanEdit)); RaiseCommands(); } } }
    public bool CanEdit => !_disposed && !IsBusy;
    public bool HasResult => _matchedPassword is not null;
    public string PasswordDisplay => !HasResult ? "" : _showPassword ? _matchedPassword! : "••••••••（点击显示 / 隐藏查看）";
    public string SearchSpace
    {
        get
        {
            if (Mode == 0) return DictionaryPath.Length > 0 ? "候选数量：导入文件逐行读取，总数未知" : $"候选数量：{PasswordCandidates.FromText(CandidateText).LongCount():N0}";
            try { return $"规则组合：{PasswordCandidates.CountRules(Charset, int.Parse(MinimumLength), int.Parse(MaximumLength)):N0} 个候选"; }
            catch { return "规则无效：未知长度须为 0—12，最大长度不小于最小长度。"; }
        }
    }

    public void HandleDroppedPaths(string[] paths)
    {
        if (!CanEdit) return;
        if (paths.Length != 1 || !File.Exists(paths[0]) || Directory.Exists(paths[0])) { Status = "请拖入单个压缩包文件，不支持文件夹或多个文件。"; return; }
        ArchivePath = paths[0];
        _ = IdentifyAsync(paths[0]);
    }
    private async Task IdentifyAsync(string path)
    {
        Status = "识别中…";
        string? format;
        try { format = await Task.Run(() => ArchiveFormatDetector.Detect(path)).WaitAsync(TimeSpan.FromSeconds(3)); }
        catch { format = null; }
        if (!_disposed && !IsBusy && ArchivePath == path)
            Status = format is null ? "无法识别支持的完整压缩包；不支持分卷、自解压或其他格式。" : $"已识别 {format} 压缩包，就绪；不会自动开始找回。";
    }
    public void ReportDropUnavailable() => Status = "当前无法拖入，请使用输入路径或浏览选择文件。";
    private void Browse()
    {
        var dialog = new OpenFileDialog { Title = "选择需要找回密码的压缩包", Filter = "压缩包|*.zip;*.7z;*.rar|所有文件|*.*", CheckFileExists = true };
        if (dialog.ShowDialog() == true) HandleDroppedPaths([dialog.FileName]);
    }
    private void Import()
    {
        var dialog = new OpenFileDialog { Title = "导入 UTF-8 候选密码（每行一个）", Filter = "文本文件|*.txt|所有文件|*.*", CheckFileExists = true };
        if (dialog.ShowDialog() == true) DictionaryPath = dialog.FileName;
    }

    public async Task StartAsync()
    {
        if (!CanEdit) return;
        if (!File.Exists(ArchivePath)) { Status = "压缩包文件不存在或无法读取。"; return; }
        if (!int.TryParse(TimeoutSeconds, out int seconds) || seconds is < 1 or > 300) { Status = "单次验证时限须为 1—300 秒。"; return; }
        using var runCancellation = new CancellationTokenSource();
        IEnumerable<string> candidates;
        try
        {
            candidates = Mode == 0
                ? DictionaryPath.Length > 0 ? PasswordCandidates.FromFile(DictionaryPath, runCancellation.Token) : PasswordCandidates.FromText(CandidateText, runCancellation.Token)
                : PasswordCandidates.FromRules(Prefix, Suffix, Charset, int.Parse(MinimumLength), int.Parse(MaximumLength), runCancellation.Token);
            if (Mode != 0) _ = PasswordCandidates.CountRules(Charset, int.Parse(MinimumLength), int.Parse(MaximumLength));
        }
        catch { Status = "候选设置无效，请检查规则长度、字符集或候选文件。"; return; }
        string path = ArchivePath;
        ClearResult(); Warning = "";
        _cancellation = runCancellation;
        IsBusy = true;
        Status = "运行中：仅本地验证，不会修改或解压原包。";
        ProgressText = "已尝试 0 个候选";
        long run = ++_run;
        var progress = new Progress<ArchiveRecoveryProgress>(value => { if (!_disposed && _run == run && IsBusy) SetProgress(value); });
        try
        {
            var verifier = _createVerifier();
            _activeVerifier = verifier as IDisposable;
            var result = await Task.Run(() => new ArchivePasswordRecoveryRunner(verifier).RunAsync(path, candidates, TimeSpan.FromSeconds(seconds), progress, _cancellation.Token));
            if (_disposed) return;
            SetProgress(new(result.Attempted, result.Elapsed, result.UncertainCount));
            _matchedPassword = result.MatchedPassword;
            Status = result.Verification.Message;
            if (result.Verification.Outcome == ArchivePasswordOutcome.Match) Status = "找到密码：受保护数据已完整校验通过。";
            if (result.Attempted == 0) Status = "没有可用候选密码，请输入或导入后再开始。";
            NotifyResult();
        }
        catch (OperationCanceledException) { if (!_disposed) Status = "已停止。再次开始会从第一个候选重新尝试。"; }
        catch { if (!_disposed) Status = "任务失败：请检查候选文件是否为有效 UTF-8、路径权限及规则设置。"; }
        finally
        {
            _activeVerifier?.Dispose(); _activeVerifier = null;
            _cancellation?.Dispose(); _cancellation = null;
            IsBusy = false;
        }
    }
    private long _run;
    private void SetProgress(ArchiveRecoveryProgress value)
    {
        ProgressText = $"已尝试 {value.Attempted:N0}  ·  {value.Attempted / Math.Max(.001, value.Elapsed.TotalSeconds):F1} 次/秒  ·  耗时 {value.Elapsed:hh\\:mm\\:ss}";
        if (value.UncertainCount > 0) Warning = $"{value.UncertainCount:N0} 次结果不确定：密码可能不符或压缩包损坏；已继续尝试其他候选。";
    }
    public void Stop()
    {
        if (!IsBusy) return;
        Status = "停止中…";
        _cancellation?.Cancel();
        StopCommand.RaiseCanExecuteChanged();
    }
    private void Copy()
    {
        if (_matchedPassword is null) return;
        try { Clipboard.SetText(_matchedPassword); Status = "密码已复制到剪贴板，请注意剪贴板历史记录与隐私。"; }
        catch { Status = "剪贴板暂时不可用，请稍后重试。"; }
    }
    private void ChangedInput() { ClearResult(); OnPropertyChanged(nameof(SearchSpace)); }
    private void ClearResult() { _matchedPassword = null; _showPassword = false; NotifyResult(); }
    private void NotifyResult() { OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(PasswordDisplay)); RevealCommand.RaiseCanExecuteChanged(); CopyCommand.RaiseCanExecuteChanged(); }
    private void RaiseCommands() { StartCommand.RaiseCanExecuteChanged(); StopCommand.RaiseCanExecuteChanged(); BrowseCommand.RaiseCanExecuteChanged(); ImportCommand.RaiseCanExecuteChanged(); ClearImportCommand.RaiseCanExecuteChanged(); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation?.Cancel(); _activeVerifier?.Dispose();
        ClearResult(); _candidateText = ""; _prefix = ""; _suffix = "";
        OnPropertyChanged(nameof(CanEdit)); RaiseCommands();
    }
}
