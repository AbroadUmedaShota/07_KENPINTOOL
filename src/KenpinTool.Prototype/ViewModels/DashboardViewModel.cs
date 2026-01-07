using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Forms;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KenpinTool.Prototype;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly DatabaseService _database;
    private readonly CaseLoader _caseLoader; // Added
    private CaseRecord? _selectedCase;
    private string _statusMessage = "";

    public DashboardViewModel(DatabaseService database, CaseLoader caseLoader)
    {
        _database = database;
        _caseLoader = caseLoader;
        
        LoadCasesCommand = new RelayCommand(LoadCases);
        CreateCaseCommand = new RelayCommand(CreateCase);
        ResumeCaseCommand = new RelayCommand(ResumeCase, () => SelectedCase is not null);
        AddFolderToCaseCommand = new RelayCommand<CaseRecord>(AddFolderToCase);
    }

    public ObservableCollection<CaseRecord> Cases { get; } = new();

    public CaseRecord? SelectedCase
    {
        get => _selectedCase;
        set
        {
            if (SetProperty(ref _selectedCase, value))
            {
                ResumeCaseCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public IRelayCommand LoadCasesCommand { get; }
    public IRelayCommand CreateCaseCommand { get; }
    public IRelayCommand ResumeCaseCommand { get; }
    public IRelayCommand<CaseRecord> AddFolderToCaseCommand { get; }

    // 画面遷移イベント
    public event EventHandler<CaseRecord>? RequestOpenInspection;

    public void Initialize()
    {
        _database.Initialize();
        LoadCases();
    }

    private void LoadCases()
    {
        try
        {
            // Log load start
            // File.AppendAllText ... (Removed)

            Cases.Clear();
            var list = _database.GetCases();
            foreach (var item in list)
            {
                Cases.Add(item);
            }
            StatusMessage = $"案件一覧をロードしました ({Cases.Count}件)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"ロードエラー: {ex.Message}";
        }
    }

    private void CreateCase()
    {
        var dialog = new Views.InputBoxDialog("新規案件名を入力してください", $"案件_{DateTime.Now:yyyyMMdd}");
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var caseName = dialog.ViewModel.InputText.Trim();
        if (string.IsNullOrWhiteSpace(caseName))
        {
            StatusMessage = "案件名が空です。";
            return;
        }

        try
        {
            // Create empty case
            // Note: InputPath is unique constraint in DB currently. 
            // We use a dummy unique path or empty string if allowed.
            // For now, let's use a special prefix to indicate "Manual Created"
            var dummyPath = $"MANUAL:{Guid.NewGuid()}"; 
            var id = _database.GetOrCreateCase(caseName, dummyPath, "prototype-v0", "open");
            
            var newCase = new CaseRecord(id, caseName, "", "open", DateTimeOffset.UtcNow, Array.Empty<FolderRecord>());
            RequestOpenInspection?.Invoke(this, newCase);
        }
        catch (Exception ex)
        {
            StatusMessage = $"案件作成エラー: {ex.Message}";
        }
    }

    private void ResumeCase()
    {
        if (SelectedCase is null)
        {
            return;
        }

        RequestOpenInspection?.Invoke(this, SelectedCase);
    }

    private void AddFolderToCase(CaseRecord? record)
    {
        if (record is null) return;

        using var dialog = new FolderBrowserDialog
        {
            Description = $"案件「{record.Name}」に追加するフォルダを選択してください",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var path = dialog.SelectedPath;
            try
            {
                // Load files from folder
                var pageSources = _caseLoader.LoadPages(path);
                if (pageSources.Count == 0)
                {
                    StatusMessage = "画像/PDFが見つかりませんでした。";
                    return;
                }

                // Get current max index for this case (to append)
                // Since DatabaseService doesn't have "GetMaxIndex", we iterate or fetch count.
                // For prototype, let's just use a large enough index or 1-based from folder.
                // Better: Modify UpsertPages or just fetch current pages first.
                // Here we cheat: DatabaseService.UpsertPages uses PageItem.Index as key.
                // We need unique indexes.
                
                // Fetch current pages to find max index
                // (Optimized way would be a DB query, but let's reuse GetCases logic or similar if possible, 
                // but GetCases only returns FolderRecords. We need page count.)
                
                var currentCount = record.Folders.Sum(f => f.PageCount);
                var startIndex = currentCount + 1;

                var pages = new List<PageItem>();
                foreach (var source in pageSources)
                {
                    pages.Add(new PageItem(startIndex, source.FilePath, Array.Empty<Detection>(), source.PdfPageIndex));
                    startIndex++;
                }

                // Register to DB
                _database.UpsertPages(record.Id, pages);
                
                StatusMessage = $"案件「{record.Name}」に {pages.Count} ページ追加しました。";
                LoadCases(); // Refresh list
            }
            catch (Exception ex)
            {
                StatusMessage = $"フォルダ追加エラー: {ex.Message}";
            }
        }
    }
}
