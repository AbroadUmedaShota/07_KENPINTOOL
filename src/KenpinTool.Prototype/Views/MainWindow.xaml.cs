using System.Windows;
using KenpinTool.Prototype.ViewModels;

namespace KenpinTool.Prototype.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
