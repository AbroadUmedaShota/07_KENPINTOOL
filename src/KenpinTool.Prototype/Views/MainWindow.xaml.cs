using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace KenpinTool.Prototype.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.ExceptionDialogRequested += ViewModel_ExceptionDialogRequested;
        Closed += (s, e) => viewModel.ExceptionDialogRequested -= ViewModel_ExceptionDialogRequested;
        PreviewGotKeyboardFocus += OnPreviewKeyboardFocusChanged;
        PreviewLostKeyboardFocus += OnPreviewKeyboardFocusChanged;
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

    private void OnPreviewKeyboardFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        var target = e.NewFocus as DependencyObject;
        var isTextInput = target is not null && IsTextInputElement(target);
        if (DataContext is MainViewModel vm)
        {
            vm.UpdateTextInputFocus(isTextInput);
        }
    }

    private static bool IsTextInputElement(DependencyObject element)
    {
        var current = element;
        while (current is not null)
        {
            if (current is TextBoxBase || current is PasswordBox)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}
