using System.Windows;

namespace KenpinTool.Prototype.Views;

public partial class InputBoxDialog : Window
{
    public InputBoxViewModel ViewModel { get; }

    public InputBoxDialog(string message, string defaultText = "")
    {
        InitializeComponent();
        ViewModel = new InputBoxViewModel { Message = message, InputText = defaultText };
        DataContext = ViewModel;
        InputTextBox.Focus();
        InputTextBox.SelectAll();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}

public class InputBoxViewModel
{
    public string Message { get; set; } = "";
    public string InputText { get; set; } = "";
}
