using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KenpinTool.Prototype;

public sealed class CompletionViewModel : ObservableObject
{
    private bool _dialogResult;

    public CompletionViewModel(int total, int ok, int ng, int exception)
    {
        TotalCount = total;
        OkCount = ok;
        NgCount = ng;
        ExceptionCount = exception;

        ConfirmCommand = new RelayCommand(() => DialogResult = true);
        CancelCommand = new RelayCommand(() => DialogResult = false);
    }

    public int TotalCount { get; }
    public int OkCount { get; }
    public int NgCount { get; }
    public int ExceptionCount { get; }

    public bool DialogResult
    {
        get => _dialogResult;
        set => SetProperty(ref _dialogResult, value);
    }

    public IRelayCommand ConfirmCommand { get; }
    public IRelayCommand CancelCommand { get; }
}
