using System;
using System.Linq;
using System.Windows;

namespace KenpinTool.Prototype;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _viewModel.ExceptionDialogRequested += ViewModel_OnExceptionDialogRequested;

        Loaded += (_, _) =>
        {
            var initialPath = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault();
            _viewModel.Initialize(initialPath);
        };

        Closed += (_, _) => _viewModel.Dispose();
    }

    private void ViewModel_OnExceptionDialogRequested(object? sender, ExceptionDialogRequest request)
    {
        var dialog = new ExceptionDialog(request.ReasonCodeOptions)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _viewModel.ApplyExceptionDecision(dialog.SelectedReasonCode, dialog.Note);
    }
}
