using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KenpinTool.Prototype;

public partial class ExceptionDialog : Window
{
    private readonly ExceptionDialogViewModel _viewModel;

    public ExceptionDialog(IReadOnlyList<string> reasonCodeOptions)
    {
        InitializeComponent();

        _viewModel = new ExceptionDialogViewModel(reasonCodeOptions);
        DataContext = _viewModel;
    }

    public string SelectedReasonCode => _viewModel.SelectedReasonCode;
    public string? Note => string.IsNullOrWhiteSpace(_viewModel.Note) ? null : _viewModel.Note;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.SelectedReasonCode))
        {
            MessageBox.Show(this, "理由コードを選択してください。", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }
}

internal sealed class ExceptionDialogViewModel : ObservableObject
{
    private string _selectedReasonCode = "";
    private string _note = "";

    public ExceptionDialogViewModel(IReadOnlyList<string>? reasonCodeOptions)
    {
        var options = reasonCodeOptions ?? Array.Empty<string>();
        ReasonCodes = options.Count == 0
            ? new[] { "EXC-01: (サンプル) 承認理由" }
            : options;

        _selectedReasonCode = ReasonCodes.Count > 0 ? ReasonCodes[0] : "";
    }

    public IReadOnlyList<string> ReasonCodes { get; }

    public string SelectedReasonCode
    {
        get => _selectedReasonCode;
        set => SetProperty(ref _selectedReasonCode, value);
    }

    public string Note
    {
        get => _note;
        set => SetProperty(ref _note, value);
    }
}
