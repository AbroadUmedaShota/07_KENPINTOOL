using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KenpinTool.Prototype.Services;

namespace KenpinTool.Prototype;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private static readonly IReadOnlyList<string> DefaultExceptionReasonCodes = new[]
    {
        "EXC-01: 再取得不可",
        "EXC-02: 依頼元承認済",
        "EXC-03: 仕様上許容",
    };

    private readonly ImageLoaderService _imageLoader;
    private readonly CaseLoader _caseLoader;
    private readonly DummyDetectionService _dummyDetector;
    private readonly QualityDetectionService _qualityDetector;
    private readonly StructureDetectionService _structureDetector;
    private readonly ReportGenerator _reportGenerator;
    private readonly object _hashLock = new();
    private readonly List<PageHash> _pageHashes = new();

    private const double DuplicateSimilarityThreshold = 0.95;
    private bool _isTextInputFocused;
    private readonly Channel<DetectionRequest> _detectChannel;
    private readonly CancellationTokenSource _detectCts = new();
    private readonly Task _detectTask;
    private readonly SynchronizationContext? _uiContext;

    private CancellationTokenSource? _imageLoadCts;
    private CancellationTokenSource? _analysisCts;
    private int _analysisTotal;
    private int _analysisCompleted;
    private int _analysisRunId;
    private DateTimeOffset _lastPagesRefresh = DateTimeOffset.MinValue;
    private int _pendingRefreshCount;

    private const int PagesRefreshBatchSize = 10;
    private const int PagesRefreshMinIntervalMs = 250;

    private RunContext? _runContext;
    private AuditLogWriter? _auditLog;
    private DatabaseService? _database;
    private int _caseId;
    private readonly Dictionary<int, int> _pageIdByIndex = new();
    private string _dbLocationLabel = "";

    private string _inputFolderPath = "";
    private string _caseName = "";
    private string _statusMessage = "フォルダを入力して Load";
    private bool _showNgOnly;
    private bool _compareMode;
    private double _zoom = 1.0;

    private PageItem? _selectedPage;
    private BitmapSource? _currentImage;
    private BitmapSource? _previousImage;

    public MainViewModel(
        ImageLoaderService imageLoader,
        CaseLoader caseLoader,
        DummyDetectionService dummyDetector,
        QualityDetectionService qualityDetector,
        StructureDetectionService structureDetector,
        ReportGenerator reportGenerator)
    {
        _imageLoader = imageLoader;
        _caseLoader = caseLoader;
        _dummyDetector = dummyDetector;
        _qualityDetector = qualityDetector;
        _structureDetector = structureDetector;
        _reportGenerator = reportGenerator;
        _uiContext = SynchronizationContext.Current;

        _detectChannel = Channel.CreateUnbounded<DetectionRequest>();
        _detectTask = Task.Run(ProcessDetectionQueueAsync);

        PagesView = CollectionViewSource.GetDefaultView(Pages);
        PagesView.Filter = FilterPages;

        _zoomTransform = new ScaleTransform(_zoom, _zoom);

        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !string.IsNullOrWhiteSpace(InputFolderPath));
        NextPageCommand = new RelayCommand(NextPage, () => Pages.Count > 0 && !_isTextInputFocused);
        PrevPageCommand = new RelayCommand(PrevPage, () => Pages.Count > 0 && !_isTextInputFocused);
        NextIssuePageCommand = new RelayCommand(NextIssuePage, () => Pages.Count > 0 && !_isTextInputFocused);
        MarkOkCommand = new RelayCommand(MarkOk, CanMarkOk);
        MarkRescanCommand = new RelayCommand(MarkRescan, () => SelectedPage is not null && !_isTextInputFocused);
        RequestExceptionCommand = new RelayCommand(RequestException, CanRequestException);
        ToggleCompareCommand = new RelayCommand(ToggleCompare, () => Pages.Count > 0 && !_isTextInputFocused);
        ToggleFilterCommand = new RelayCommand(ToggleFilter, () => Pages.Count > 0 && !_isTextInputFocused);
        ToggleZoomCommand = new RelayCommand(ToggleZoom, () => SelectedPage is not null && !_isTextInputFocused);
        ExportCsvCommand = new RelayCommand(ExportCsv, () => Pages.Count > 0 && _runContext is not null);
        ExportReportCommand = new AsyncRelayCommand(ExportReportAsync, () => Pages.Count > 0 && _runContext is not null);
    }

    public event EventHandler<ExceptionDialogRequest>? ExceptionDialogRequested;

    public ObservableCollection<PageItem> Pages { get; } = new();

    public ICollectionView PagesView { get; }

    public string InputFolderPath
    {
        get => _inputFolderPath;
        set
        {
            if (SetProperty(ref _inputFolderPath, value))
            {
                ((AsyncRelayCommand)LoadCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string CaseName
    {
        get => _caseName;
        private set => SetProperty(ref _caseName, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool ShowNgOnly
    {
        get => _showNgOnly;
        set
        {
            if (SetProperty(ref _showNgOnly, value))
            {
                PagesView.Refresh();
                UpdateCommandStates();
            }
        }
    }

    public bool CompareMode
    {
        get => _compareMode;
        set
        {
            if (SetProperty(ref _compareMode, value))
            {
                _ = RefreshImagesAsync();
            }
        }
    }

    public double Zoom
    {
        get => _zoom;
        set
        {
            if (!SetProperty(ref _zoom, value))
            {
                return;
            }

            _zoomTransform.ScaleX = _zoom;
            _zoomTransform.ScaleY = _zoom;
        }
    }

    private readonly ScaleTransform _zoomTransform;
    public ScaleTransform ZoomTransform => _zoomTransform;

    public PageItem? SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (!SetProperty(ref _selectedPage, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedPageText));
            OnPropertyChanged(nameof(SelectedDecisionText));
            UpdateCommandStates();
            _ = RefreshImagesAsync();
        }
    }

    public BitmapSource? CurrentImage
    {
        get => _currentImage;
        private set => SetProperty(ref _currentImage, value);
    }

    public BitmapSource? PreviousImage
    {
        get => _previousImage;
        private set => SetProperty(ref _previousImage, value);
    }

    public ObservableCollection<OverlayRect> CurrentOverlays { get; } = new();

    public string SelectedPageText
    {
        get
        {
            if (SelectedPage is null)
            {
                return "";
            }

            var pdfSuffix = SelectedPage.PdfPageIndex.HasValue
                ? $" (PDF p{SelectedPage.PdfPageIndex:000})"
                : "";
            return $"Page {SelectedPage.Index:000}/{Pages.Count:000}  {SelectedPage.FileName}{pdfSuffix}";
        }
    }

    public string SelectedDecisionText
    {
        get
        {
            if (SelectedPage?.Decision is null)
            {
                if (SelectedPage?.HasQlT05ActiveDetections == true)
                {
                    return "QLT-05（線状ノイズ）検出: 再スキャンのみ選択可";
                }

                return "未判定";
            }

            return SelectedPage.Decision.Action switch
            {
                DecisionAction.Ok => "OK",
                DecisionAction.Rescan => "再スキャン（NG-A）",
                DecisionAction.ExceptionApproved => $"例外承認: {SelectedPage.Decision.ExceptionReasonCode} {SelectedPage.Decision.ExceptionNote}",
                _ => "OK",
            };
        }
    }

    public string ProgressText
        => Pages.Count == 0 ? "" : $"{Pages.Count(p => p.IsReviewed)}/{Pages.Count} reviewed";

    public string PageCountText
        => Pages.Count == 0 ? "" : $"{Pages.Count} pages";

    public string OutputDirectoryText
        => _runContext is null ? "" : $"Output: {_runContext.OutputDirectory}";

    public IRelayCommand LoadCommand { get; }
    public IRelayCommand NextPageCommand { get; }
    public IRelayCommand PrevPageCommand { get; }
    public IRelayCommand NextIssuePageCommand { get; }
    public IRelayCommand MarkOkCommand { get; }
    public IRelayCommand MarkRescanCommand { get; }
    public IRelayCommand RequestExceptionCommand { get; }
    public IRelayCommand ToggleCompareCommand { get; }
    public IRelayCommand ToggleFilterCommand { get; }
    public IRelayCommand ToggleZoomCommand { get; }
    public IRelayCommand ExportCsvCommand { get; }
    public IAsyncRelayCommand ExportReportCommand { get; }

    public void Initialize(string? initialFolderPath)
    {
        if (!string.IsNullOrWhiteSpace(initialFolderPath) && Directory.Exists(initialFolderPath))
        {
            InputFolderPath = initialFolderPath;
            _ = LoadAsync();
        }
    }

    public void UpdateTextInputFocus(bool isTextInputFocused)
    {
        if (_isTextInputFocused == isTextInputFocused)
        {
            return;
        }

        _isTextInputFocused = isTextInputFocused;
        UpdateCommandStates();
    }

    public void ApplyExceptionDecision(string reasonCode, string? note)
    {
        if (SelectedPage is null)
        {
            return;
        }

        if (SelectedPage.HasFatalActiveDetections || SelectedPage.HasQlT05ActiveDetections)
        {
            StatusMessage = "NG-Aの例外承認は不可です。";
            return;
        }

        SelectedPage.ApplyException(reasonCode, note);
        AppendDecisionLog(SelectedPage);
        PersistDecision(SelectedPage);

        OnPropertyChanged(nameof(SelectedDecisionText));
        OnPropertyChanged(nameof(ProgressText));

        PagesView.Refresh();
        UpdateCommandStates();
        _ = RefreshImagesAsync();

        NextPage();
    }

    private async Task LoadAsync()
    {
        try
        {
            StatusMessage = "ロード中...";

            DisposeRun();

            var folderPath = InputFolderPath.Trim();
            await Task.Yield();

            var pageSources = await Task.Run(() => _caseLoader.LoadPages(folderPath));
            if (pageSources.Count == 0)
            {
                StatusMessage = "画像/PDFが見つかりませんでした。";
                return;
            }

            _runContext = RunContext.Create(folderPath);
            _auditLog = new AuditLogWriter(_runContext.AuditLogPath);
            _database = new DatabaseService(_runContext.DbPath, _runContext.DbFallbackPath);
            _database.Initialize();
            _dbLocationLabel = _database.IsFallback ? "出力フォルダ" : "入力フォルダ";
            _caseId = _database.GetOrCreateCase(_runContext.CaseName, folderPath, "prototype-v0", "open");

            var caseMeta = new
            {
                caseName = _runContext.CaseName,
                inputFolderPath = _runContext.InputFolderPath,
                openedAtUtc = DateTimeOffset.UtcNow,
                ruleset = "prototype-v0",
                pageCount = pageSources.Count,
            };
            File.WriteAllText(
                _runContext.CaseJsonPath,
                JsonSerializer.Serialize(caseMeta, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            _auditLog.Append("case_opened", caseMeta);

            var pages = await Task.Run(() =>
            {
                var result = new List<PageItem>(pageSources.Count);
                var index = 1;
                foreach (var source in pageSources)
                {
                    result.Add(new PageItem(index, source.FilePath, Array.Empty<Detection>(), source.PdfPageIndex));
                    index++;
                }

                return result;
            });

            Pages.Clear();

            foreach (var p in pages)
            {
                Pages.Add(p);
            }

            _pageIdByIndex.Clear();
            foreach (var kvp in _database.UpsertPages(_caseId, pages))
            {
                _pageIdByIndex[kvp.Key] = kvp.Value;
            }

            var detectionsMap = _database.LoadDetections(_caseId);
            var decisionsMap = _database.LoadDecisions(_caseId);
            var pagesToAnalyze = new List<PageItem>();

            foreach (var page in pages)
            {
                if (detectionsMap.TryGetValue(page.Index, out var detections))
                {
                    SetDetections(page, detections);
                }

                if (decisionsMap.TryGetValue(page.Index, out var decision))
                {
                    page.RestoreDecision(decision);
                }

                if (!detectionsMap.ContainsKey(page.Index) && !page.IsReviewed)
                {
                    pagesToAnalyze.Add(page);
                }
            }

            CaseName = _runContext.CaseName;
            StatusMessage = BuildStatusMessage($"読み込み完了: {Pages.Count}ページ");

            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(PageCountText));
            OnPropertyChanged(nameof(OutputDirectoryText));

            SelectedPage = Pages.FirstOrDefault();
            UpdateCommandStates();

            StartAnalysis(pagesToAnalyze);
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
        }
    }

    private bool FilterPages(object obj)
    {
        if (!ShowNgOnly)
        {
            return true;
        }

        if (obj is not PageItem page)
        {
            return false;
        }

        if (page.Decision is null)
        {
            return page.HasActiveDetections;
        }

        return page.Decision.Action != DecisionAction.Ok;
    }

    private void NextPage()
    {
        if (SelectedPage is null || Pages.Count == 0)
        {
            return;
        }

        var idx = Pages.IndexOf(SelectedPage);
        if (idx < 0)
        {
            return;
        }

        var nextIdx = Math.Min(idx + 1, Pages.Count - 1);
        SelectedPage = Pages[nextIdx];
    }

    private void PrevPage()
    {
        if (SelectedPage is null || Pages.Count == 0)
        {
            return;
        }

        var idx = Pages.IndexOf(SelectedPage);
        if (idx < 0)
        {
            return;
        }

        var prevIdx = Math.Max(idx - 1, 0);
        SelectedPage = Pages[prevIdx];
    }

    private void NextIssuePage()
    {
        if (SelectedPage is null || Pages.Count == 0)
        {
            return;
        }

        var start = Pages.IndexOf(SelectedPage);
        if (start < 0)
        {
            return;
        }

        for (var i = 1; i <= Pages.Count; i++)
        {
            var idx = (start + i) % Pages.Count;
            var candidate = Pages[idx];
            if (candidate.Decision is null && candidate.HasActiveDetections)
            {
                SelectedPage = candidate;
                return;
            }
        }

        StatusMessage = "次のNG候補がありません。";
    }

    private bool CanMarkOk()
    {
        if (_isTextInputFocused)
        {
            return false;
        }

        if (SelectedPage is null)
        {
            return false;
        }

        return !SelectedPage.HasFatalActiveDetections;
    }

    private void MarkOk()
    {
        if (SelectedPage is null)
        {
            return;
        }

        if (SelectedPage.HasFatalActiveDetections)
        {
            StatusMessage = "NG-Aは再スキャンのみ選択可能です。";
            return;
        }

        SelectedPage.ApplyOk();
        AppendDecisionLog(SelectedPage);
        PersistDecision(SelectedPage);

        OnPropertyChanged(nameof(SelectedDecisionText));
        OnPropertyChanged(nameof(ProgressText));

        PagesView.Refresh();
        UpdateCommandStates();
        _ = RefreshImagesAsync();

        NextPage();
    }

    private void MarkRescan()
    {
        if (SelectedPage is null)
        {
            return;
        }

        SelectedPage.ApplyRescan();
        AppendDecisionLog(SelectedPage);
        PersistDecision(SelectedPage);

        OnPropertyChanged(nameof(SelectedDecisionText));
        OnPropertyChanged(nameof(ProgressText));

        PagesView.Refresh();
        UpdateCommandStates();
        _ = RefreshImagesAsync();

        NextPage();
    }

    private bool CanRequestException()
    {
        if (_isTextInputFocused)
        {
            return false;
        }

        if (SelectedPage is null)
        {
            return false;
        }

        if (SelectedPage.HasQlT05ActiveDetections)
        {
            return false;
        }

        return !SelectedPage.HasFatalActiveDetections;
    }

    private void RequestException()
    {
        if (!CanRequestException())
        {
            StatusMessage = "例外承認はNG-B/NG-Cのみ可能です。";
            return;
        }

        ExceptionDialogRequested?.Invoke(this, new ExceptionDialogRequest(DefaultExceptionReasonCodes));
    }

    private void ToggleCompare()
    {
        CompareMode = !CompareMode;
        StatusMessage = CompareMode ? "比較モード: ON" : "比較モード: OFF";
    }

    private void ToggleFilter()
    {
        ShowNgOnly = !ShowNgOnly;
        StatusMessage = ShowNgOnly ? "絞り込み: NG/疑いのみ" : "絞り込み: 全て";
    }

    private void ToggleZoom()
    {
        Zoom = Zoom >= 2.0 ? 1.0 : 2.0;
    }

    private void ExportCsv()
    {
        if (_runContext is null)
        {
            StatusMessage = "案件が未ロードです。";
            return;
        }

        try
        {
            CsvExporter.Export(_runContext.CsvPath, Pages);
            _auditLog?.Append("csv_exported", new { csvPath = _runContext.CsvPath });
            StatusMessage = $"CSV出力完了: {_runContext.CsvPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"CSV出力に失敗しました: {ex.Message}";
        }
    }

    private async Task ExportReportAsync()
    {
        if (_runContext is null)
        {
            StatusMessage = "案件が未ロードです。";
            return;
        }

        try
        {
            var metadata = BuildReportMetadata();
            var issueItems = BuildReportIssues();
            var outputPath = Path.Combine(_runContext.OutputDirectory, "report.pdf");

            StatusMessage = BuildStatusMessage("レポート生成中...");

            await Task.Run(() => _reportGenerator.Generate(outputPath, metadata, issueItems));

            _auditLog?.Append("report_exported", new { reportPath = outputPath, issueCount = issueItems.Count });
            StatusMessage = $"レポート出力完了: {outputPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"レポート出力に失敗しました: {ex.Message}";
        }
    }

    private ReportMetadata BuildReportMetadata()
    {
        var okCount = Pages.Count(p => p.Decision?.Action == DecisionAction.Ok);
        var rescanCount = Pages.Count(p => p.Decision?.Action == DecisionAction.Rescan);
        var exceptionCount = Pages.Count(p => p.Decision?.Action == DecisionAction.ExceptionApproved);
        var unreviewedCount = Pages.Count - okCount - rescanCount - exceptionCount;

        var version = typeof(MainViewModel).Assembly.GetName().Version?.ToString() ?? "unknown";

        return new ReportMetadata(
            CaseName,
            _runContext?.InputFolderPath ?? "",
            DateTimeOffset.Now,
            version,
            Pages.Count,
            okCount,
            rescanCount,
            exceptionCount,
            unreviewedCount);
    }

    private List<ReportIssueItem> BuildReportIssues()
    {
        var items = new List<ReportIssueItem>();

        foreach (var page in Pages)
        {
            if (page.Decision?.Action is not DecisionAction.Rescan and not DecisionAction.ExceptionApproved)
            {
                continue;
            }

            var detections = page.Detections
                .Select(d => new ReportDetection(d.Code, d.Level, d.Evidence.ToArray()))
                .ToList();

            items.Add(
                new ReportIssueItem(
                    page.Index,
                    page.FilePath,
                    page.FileName,
                    page.PdfPageIndex,
                    page.Decision.Action,
                    page.Decision.ExceptionReasonCode,
                    page.Decision.ExceptionNote,
                    detections));
        }

        return items;
    }

    private void AppendDecisionLog(PageItem page)
    {
        if (_auditLog is null || _runContext is null)
        {
            return;
        }

        var decision = page.Decision;
        if (decision is null)
        {
            return;
        }

        _auditLog.Append(
            "decision",
            new
            {
                pageIndex = page.Index,
                fileName = page.FileName,
                action = decision.Action.ToString(),
                tsUtc = decision.TimestampUtc,
                exceptionReasonCode = decision.ExceptionReasonCode,
                exceptionNote = decision.ExceptionNote,
                pdfPageIndex = page.PdfPageIndex,
                ngCodes = page.Detections.Select(d => d.Code).ToArray(),
            });
    }

    private async Task RefreshImagesAsync()
    {
        var page = SelectedPage;
        if (page is null)
        {
            CurrentImage = null;
            PreviousImage = null;
            CurrentOverlays.Clear();
            return;
        }

        var cts = new CancellationTokenSource();
        var prev = Interlocked.Exchange(ref _imageLoadCts, cts);
        prev?.Cancel();

        try
        {
            var current = await _imageLoader.LoadImageAsync(page.FilePath, page.PdfPageIndex, cts.Token);
            CurrentImage = current;

            if (CompareMode && page.Index > 1)
            {
                var prevPage = Pages.ElementAtOrDefault(page.Index - 2);
                PreviousImage = prevPage is null
                    ? null
                    : await _imageLoader.LoadImageAsync(prevPage.FilePath, prevPage.PdfPageIndex, cts.Token);
            }
            else
            {
                PreviousImage = null;
            }

            if (current != null)
            {
                BuildOverlays(page, current);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = $"画像の読み込みに失敗しました: {ex.Message}";
        }
    }

    private void BuildOverlays(PageItem page, BitmapSource image)
    {
        CurrentOverlays.Clear();

        var w = image.PixelWidth;
        var h = image.PixelHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        foreach (var detection in page.ActiveDetections())
        {
            if (detection.Evidence.Count == 0)
            {
                continue;
            }

            foreach (var ev in detection.Evidence)
            {
                CurrentOverlays.Add(
                    new OverlayRect(
                        X: ev.X * w,
                        Y: ev.Y * h,
                        Width: ev.Width * w,
                        Height: ev.Height * h,
                        Stroke: detection.LevelBrush,
                        Label: detection.Code,
                        Opacity: Math.Clamp(detection.Confidence ?? 0.8, 0.5, 1.0)));
            }
        }
    }

    private void UpdateCommandStates()
    {
        NextPageCommand.NotifyCanExecuteChanged();
        PrevPageCommand.NotifyCanExecuteChanged();
        NextIssuePageCommand.NotifyCanExecuteChanged();
        MarkOkCommand.NotifyCanExecuteChanged();
        MarkRescanCommand.NotifyCanExecuteChanged();
        RequestExceptionCommand.NotifyCanExecuteChanged();
        ToggleCompareCommand.NotifyCanExecuteChanged();
        ToggleFilterCommand.NotifyCanExecuteChanged();
        ToggleZoomCommand.NotifyCanExecuteChanged();
        ExportCsvCommand.NotifyCanExecuteChanged();
        ExportReportCommand.NotifyCanExecuteChanged();
    }

    private void DisposeRun()
    {
        _auditLog?.Dispose();
        _auditLog = null;
        _runContext = null;
        CaseName = "";
        OnPropertyChanged(nameof(OutputDirectoryText));
        _analysisCts?.Cancel();
        _analysisCts?.Dispose();
        _analysisCts = null;
        _analysisTotal = 0;
        _analysisCompleted = 0;
        _database = null;
        _caseId = 0;
        _pageIdByIndex.Clear();
        _dbLocationLabel = "";
        lock (_hashLock)
        {
            _pageHashes.Clear();
        }
    }

    public void Dispose()
    {
        DisposeRun();
        _detectCts.Cancel();
        _detectChannel.Writer.TryComplete();
    }

    private void StartAnalysis(IReadOnlyList<PageItem> pages)
    {
        _analysisCts?.Cancel();
        _analysisCts?.Dispose();
        _analysisCts = new CancellationTokenSource();

        var runId = Interlocked.Increment(ref _analysisRunId);
        _analysisTotal = pages.Count;
        _analysisCompleted = 0;
        UpdateAnalysisStatus();
        lock (_hashLock)
        {
            _pageHashes.Clear();
        }

        _ = Task.Run(() => EnqueueDetectionRequestsAsync(pages, runId, _analysisCts.Token), _analysisCts.Token);
    }

    private async Task EnqueueDetectionRequestsAsync(
        IReadOnlyList<PageItem> pages,
        int runId,
        CancellationToken ct)
    {
        foreach (var page in pages)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            await _detectChannel.Writer.WriteAsync(new DetectionRequest(page, runId, ct), ct);
        }
    }

    private async Task ProcessDetectionQueueAsync()
    {
        var reader = _detectChannel.Reader;

        try
        {
            while (await reader.WaitToReadAsync(_detectCts.Token))
            {
                while (reader.TryRead(out var request))
                {
                    if (request.CancellationToken.IsCancellationRequested || request.RunId != _analysisRunId)
                    {
                        continue;
                    }

                    var detections = AnalyzeDetections(request.Page);
                    if (request.CancellationToken.IsCancellationRequested || request.RunId != _analysisRunId)
                    {
                        continue;
                    }

                    PersistDetections(request.Page, detections);

                    PostToUi(() =>
                    {
                        ApplyDetectionsToPage(request.Page, detections);
                        _analysisCompleted = Math.Min(_analysisCompleted + 1, _analysisTotal);
                        UpdateAnalysisStatus();
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private IReadOnlyList<Detection> AnalyzeDetections(PageItem page)
    {
        var detections = new List<Detection>(
            _dummyDetector
                .DetectFromFileName(page.FilePath)
                .Where(d => !d.IsQlT05 && !string.Equals(d.Code, "STR-02", StringComparison.OrdinalIgnoreCase)));

        detections.AddRange(_qualityDetector.DetectQlT05(page.FilePath, page.PdfPageIndex));

        var currentHash = _structureDetector.ComputeHash(page.FilePath, page.PdfPageIndex);
        if (currentHash is not null)
        {
            var matchPageIndex = 0;
            var matchSimilarity = 0.0;

            lock (_hashLock)
            {
                foreach (var existing in _pageHashes)
                {
                    var similarity = _structureDetector.ComputeSimilarity(currentHash, existing.Hash);
                    if (similarity >= DuplicateSimilarityThreshold && similarity > matchSimilarity)
                    {
                        matchSimilarity = similarity;
                        matchPageIndex = existing.PageIndex;
                    }
                }

                _pageHashes.Add(new PageHash(page.Index, currentHash));
            }

            if (matchPageIndex > 0)
            {
                detections.Add(
                    new Detection(
                        "STR-02",
                        $"ページ{matchPageIndex:000}と重複",
                        NgLevel.NgA,
                        SuggestedAction.Rescan,
                        ReworkType.None,
                        confidence: matchSimilarity,
                        evidence: new[] { new EvidenceRegion(0, 0, 1, 1) }));
            }
        }

        return detections;
    }

    private void ApplyDetectionsToPage(PageItem page, IReadOnlyList<Detection> detections)
    {
        if (page.IsReviewed)
        {
            return;
        }

        SetDetections(page, detections);

        if (page == SelectedPage)
        {
            OnPropertyChanged(nameof(SelectedDecisionText));
            if (CurrentImage is not null)
            {
                BuildOverlays(page, CurrentImage);
            }
        }

        MaybeRefreshPagesView();
    }

    private void SetDetections(PageItem page, IReadOnlyList<Detection> detections)
    {
        page.Detections.Clear();
        foreach (var detection in detections)
        {
            page.Detections.Add(detection);
        }
    }

    private void PersistDetections(PageItem page, IReadOnlyList<Detection> detections)
    {
        if (_database is null)
        {
            return;
        }

        if (!_pageIdByIndex.TryGetValue(page.Index, out var pageId))
        {
            return;
        }

        _database.SaveDetections(pageId, detections);
    }

    private void PersistDecision(PageItem page)
    {
        if (_database is null)
        {
            return;
        }

        if (!_pageIdByIndex.TryGetValue(page.Index, out var pageId))
        {
            return;
        }

        if (page.Decision is null)
        {
            return;
        }

        _database.SaveDecision(pageId, page.Decision);
    }

    private void UpdateAnalysisStatus()
    {
        if (_analysisTotal <= 0)
        {
            return;
        }

        var message = _analysisCompleted >= _analysisTotal
            ? $"解析完了: {_analysisCompleted}/{_analysisTotal} ページ"
            : $"解析中: {_analysisCompleted}/{_analysisTotal} ページ";

        StatusMessage = BuildStatusMessage(message);
    }

    private string BuildStatusMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(_dbLocationLabel))
        {
            return message;
        }

        return $"{message}  (DB配置: {_dbLocationLabel})";
    }

    private void MaybeRefreshPagesView()
    {
        _pendingRefreshCount++;
        var now = DateTimeOffset.UtcNow;
        var elapsed = now - _lastPagesRefresh;

        if (_pendingRefreshCount < PagesRefreshBatchSize &&
            elapsed.TotalMilliseconds < PagesRefreshMinIntervalMs)
        {
            return;
        }

        _pendingRefreshCount = 0;
        _lastPagesRefresh = now;
        PagesView.Refresh();
    }

    private void PostToUi(Action action)
    {
        if (_uiContext is not null)
        {
            _uiContext.Post(_ => action(), null);
            return;
        }

        if (Application.Current?.Dispatcher is not null)
        {
            Application.Current.Dispatcher.InvokeAsync(action);
            return;
        }

        action();
    }

    private sealed record PageHash(int PageIndex, byte[] Hash);

    private sealed record DetectionRequest(PageItem Page, int RunId, CancellationToken CancellationToken);
}
