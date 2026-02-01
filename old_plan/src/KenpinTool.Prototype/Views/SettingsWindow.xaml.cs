using System.Windows;

namespace KenpinTool.Prototype;

public partial class SettingsWindow : Window
{
    public SettingsWindow(DetectionSettings settings)
    {
        InitializeComponent();
        DataContext = settings;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DetectionSettings settings)
        {
            settings.Reset();
        }
    }
}
