using System.Windows;

namespace KenpinTool.Prototype;
public partial class DashboardWindow : Window
{
    public DashboardWindow(DashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        IsVisibleChanged += (s, e) =>
        {
            if (IsVisible)
            {
                viewModel.Initialize();
            }
        };
    }
}
