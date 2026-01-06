using System.Windows;
using KenpinTool.Prototype.ViewModels;

namespace KenpinTool.Prototype.Views;

public partial class DashboardWindow : Window
{
    public DashboardWindow(DashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (s, e) => viewModel.Initialize();
    }
}
