using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Forms;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KenpinTool.Prototype.Services;

namespace KenpinTool.Prototype.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly DatabaseService _database;
    private CaseRecord? _selectedCase;
    private string _statusMessage = "";

    public DashboardViewModel(DatabaseService database)
    {
        _database = database;
        
        // アプリ起動時に初期化が必要なので、View側でInitializeを呼んでもらうか、
        // 単純にコンストラクタで同期的にロードできる範囲でロードする。
        // ここでは空にしておき、LoadCasesCommand等でロードする方針をとる。
        
        LoadCasesCommand = new RelayCommand(LoadCases);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        ResumeCaseCommand = new RelayCommand(ResumeCase, () => SelectedCase is not null);
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
    public IRelayCommand OpenFolderCommand { get; }
    public IRelayCommand ResumeCaseCommand { get; }

    // 画面遷移イベント
    public event EventHandler<string>? RequestOpenInspection;

    public void Initialize()
    {
        _database.Initialize();
        LoadCases();
    }

    private void LoadCases()
    {
        try
        {
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

    private void OpenFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "検品対象のフォルダを選択してください",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var path = dialog.SelectedPath;
            // 新規案件として登録するロジックは本来ここで行うか、Inspection画面に渡してから行うか。
            // 今回は「パスを渡してInspection画面を開き、そこでロード＆登録」というフローにする。
            RequestOpenInspection?.Invoke(this, path);
        }
    }

    private void ResumeCase()
    {
        if (SelectedCase is null)
        {
            return;
        }

        RequestOpenInspection?.Invoke(this, SelectedCase.InputPath);
    }
}
