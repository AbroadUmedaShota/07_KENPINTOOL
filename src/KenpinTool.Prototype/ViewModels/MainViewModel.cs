using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KenpinTool.Prototype;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private static readonly IReadOnlyList<string> DefaultExceptionReasonCodes = new[]
    {
        "EXC-01: 再取得不可",
        "EXC-02: 依頼元承認済",
        "EXC-03: 仕様上許容",
    };

    private readonly CaseLoader _caseLoader = new();
    private readonly DummyDetectionService _dummyDetector = new();

    private readonly Dictionary<string, BitmapSource> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _imageCacheOrder = new();

    private CancellationTokenSource? _imageLoadCts;

    private RunContext? _runContext;
    private AuditLogWriter? _auditLog;

    private string _inputFolderPath = "";
    private string _caseName = "";
    private string _statusMessage = "フォルダを入力して Load";
    private bool _showNgOnly;
    private bool _compareMode;
    private double _zoom = 1.0;

    private PageItem? _selectedPage;
    private BitmapSource? _currentImage;
    private BitmapSource? _previousImage;

    public MainViewModel()
    {
        PagesView = CollectionViewSource.GetDefaultView(Pages);
        PagesView.Filter = FilterPages;

        _zoomTransform = new ScaleTransform(_zoom, _zoom);

        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !string.IsNullOrWhiteSpace(InputFolderPath));
        NextPageCommand = new RelayCommand(NextPage, () => Pages.Count > 0);
        PrevPageCommand = new RelayCommand(PrevPage, () => Pages.Count > 0);
        NextIssuePageCommand = new RelayCommand(NextIssuePage, () => Pages.Count > 0);
        MarkOkCommand = new RelayCommand(MarkOk, CanMarkOk);
        MarkRescanCommand = new RelayCommand(MarkRescan, () => SelectedPage is not null);
        RequestExceptionCommand = new RelayCommand(RequestException, CanRequestException);
        ToggleCompareCommand = new RelayCommand(ToggleCompare, () => Pages.Count > 0);
        ToggleFilterCommand = new RelayCommand(ToggleFilter, () => Pages.Count > 0);
        ToggleZoomCommand = new RelayCommand(ToggleZoom, () => SelectedPage is not null);
        ExportCsvCommand = new RelayCommand(ExportCsv, () => Pages.Count > 0 && _runContext is not null);
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

            return $"Page {SelectedPage.Index:000}/{Pages.Count:000}  {SelectedPage.FileName}";
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

    public System.Windows.Input.ICommand LoadCommand { get; }
    public System.Windows.Input.ICommand NextPageCommand { get; }
    public System.Windows.Input.ICommand PrevPageCommand { get; }
    public System.Windows.Input.ICommand NextIssuePageCommand { get; }
    public System.Windows.Input.ICommand MarkOkCommand { get; }
    public System.Windows.Input.ICommand MarkRescanCommand { get; }
    public System.Windows.Input.ICommand RequestExceptionCommand { get; }
    public System.Windows.Input.ICommand ToggleCompareCommand { get; }
    public System.Windows.Input.ICommand ToggleFilterCommand { get; }
    public System.Windows.Input.ICommand ToggleZoomCommand { get; }
    public System.Windows.Input.ICommand ExportCsvCommand { get; }

    public void Initialize(string? initialFolderPath)
    {
        if (!string.IsNullOrWhiteSpace(initialFolderPath) && Directory.Exists(initialFolderPath))
        {
            InputFolderPath = initialFolderPath;
            _ = LoadAsync();
        }
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
            StatusMessage = "Loading...";

            DisposeRun();

            var folderPath = InputFolderPath.Trim();
            await Task.Yield();

            var files = await Task.Run(() => _caseLoader.LoadImageFiles(folderPath));
            if (files.Count == 0)
            {
                StatusMessage = "画像が見つかりませんでした。";
                return;
            }

            _runContext = RunContext.Create(folderPath);
            _auditLog = new AuditLogWriter(_runContext.AuditLogPath);

            var caseMeta = new
            {
                caseName = _runContext.CaseName,
                inputFolderPath = _runContext.InputFolderPath,
                openedAtUtc = DateTimeOffset.UtcNow,
                ruleset = "prototype-v0",
                pageCount = files.Count,
            };
            File.WriteAllText(
                _runContext.CaseJsonPath,
                JsonSerializer.Serialize(caseMeta, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            _auditLog.Append("case_opened", caseMeta);

            var pages = await Task.Run(() =>
            {
                var result = new List<PageItem>(files.Count);
                var index = 1;
                foreach (var file in files)
                {
                    var detections = _dummyDetector.DetectFromFileName(file);
                    result.Add(new PageItem(index, file, detections));
                    index++;
                }

                return result;
            });

            Pages.Clear();
            _imageCache.Clear();
            _imageCacheOrder.Clear();

            foreach (var p in pages)
            {
                Pages.Add(p);
            }

            CaseName = _runContext.CaseName;
            StatusMessage = $"Loaded {Pages.Count} pages.";

            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(PageCountText));
            OnPropertyChanged(nameof(OutputDirectoryText));

            SelectedPage = Pages.FirstOrDefault();
            UpdateCommandStates();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
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
        if (SelectedPage is null)
        {
            return false;
        }

        return !SelectedPage.HasQlT05ActiveDetections;
    }

    private void MarkOk()
    {
        if (SelectedPage is null)
        {
            return;
        }

        if (SelectedPage.HasQlT05ActiveDetections)
        {
            StatusMessage = "QLT-05は再スキャンのみ選択可能です。";
            return;
        }

        SelectedPage.ApplyOk();
        AppendDecisionLog(SelectedPage);

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

        OnPropertyChanged(nameof(SelectedDecisionText));
        OnPropertyChanged(nameof(ProgressText));

        PagesView.Refresh();
        UpdateCommandStates();
        _ = RefreshImagesAsync();

        NextPage();
    }

    private bool CanRequestException()
    {
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
        StatusMessage = CompareMode ? "Compare: ON" : "Compare: OFF";
    }

    private void ToggleFilter()
    {
        ShowNgOnly = !ShowNgOnly;
        StatusMessage = ShowNgOnly ? "Filter: NG/疑いのみ" : "Filter: All";
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
            StatusMessage = $"CSV exported: {_runContext.CsvPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"CSV export failed: {ex.Message}";
        }
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
            var current = await LoadBitmapCachedAsync(page.FilePath, cts.Token);
            CurrentImage = current;

            if (CompareMode && page.Index > 1)
            {
                var prevPage = Pages.ElementAtOrDefault(page.Index - 2);
                PreviousImage = prevPage is null ? null : await LoadBitmapCachedAsync(prevPage.FilePath, cts.Token);
            }
            else
            {
                PreviousImage = null;
            }

            BuildOverlays(page, current);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = $"Image load failed: {ex.Message}";
        }
    }

    private async Task<BitmapSource> LoadBitmapCachedAsync(string filePath, CancellationToken cancellationToken)
    {
        if (_imageCache.TryGetValue(filePath, out var cached))
        {
            return cached;
        }

        var loaded = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(filePath);
            image.EndInit();
            image.Freeze();
            return (BitmapSource)image;
        }, cancellationToken);

        _imageCache[filePath] = loaded;
        _imageCacheOrder.AddLast(filePath);

        while (_imageCacheOrder.Count > 12)
        {
            var toRemove = _imageCacheOrder.First!.Value;
            _imageCacheOrder.RemoveFirst();
            _imageCache.Remove(toRemove);
        }

        return loaded;
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
        ((RelayCommand)NextPageCommand).NotifyCanExecuteChanged();
        ((RelayCommand)PrevPageCommand).NotifyCanExecuteChanged();
        ((RelayCommand)NextIssuePageCommand).NotifyCanExecuteChanged();
        ((RelayCommand)MarkOkCommand).NotifyCanExecuteChanged();
        ((RelayCommand)MarkRescanCommand).NotifyCanExecuteChanged();
        ((RelayCommand)RequestExceptionCommand).NotifyCanExecuteChanged();
        ((RelayCommand)ToggleCompareCommand).NotifyCanExecuteChanged();
        ((RelayCommand)ToggleFilterCommand).NotifyCanExecuteChanged();
        ((RelayCommand)ToggleZoomCommand).NotifyCanExecuteChanged();
        ((RelayCommand)ExportCsvCommand).NotifyCanExecuteChanged();
    }

    private void DisposeRun()
    {
        _auditLog?.Dispose();
        _auditLog = null;
        _runContext = null;
        CaseName = "";
        OnPropertyChanged(nameof(OutputDirectoryText));
    }

    public void Dispose()
    {
        DisposeRun();
    }
}
