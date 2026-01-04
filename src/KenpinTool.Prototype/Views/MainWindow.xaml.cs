using System.Windows;

namespace KenpinTool.Prototype.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.ExceptionDialogRequested += ViewModel_ExceptionDialogRequested;
        Closed += (s, e) => viewModel.ExceptionDialogRequested -= ViewModel_ExceptionDialogRequested;
    }

    private void ViewModel_ExceptionDialogRequested(object? sender, ExceptionDialogRequest e)
    {
        var dialog = new ExceptionDialog(e.ReasonCodeOptions)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ApplyExceptionDecision(dialog.SelectedReasonCode, dialog.Note);
            }
        }
    }
}