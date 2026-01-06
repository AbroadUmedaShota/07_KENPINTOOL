using System.Windows;
using KenpinTool.Prototype.ViewModels;

namespace KenpinTool.Prototype.Views;

public partial class CompletionDialog : Window
{
    public CompletionDialog(CompletionViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // ViewModel の DialogResult 変更を検知してウィンドウを閉じる
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CompletionViewModel.DialogResult))
            {
                DialogResult = viewModel.DialogResult;
                Close();
            }
        };
    }
}
