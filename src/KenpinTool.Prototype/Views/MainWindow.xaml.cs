using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
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
        Loaded += OnLoaded;
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateDisplayDpi();
        UpdateViewportSize();
    }

    private void ImageScrollHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateViewportSize();
    }

    private void ImageScrollHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var oldZoom = vm.Zoom;
        var factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        vm.AdjustZoom(factor);

        if (Math.Abs(vm.Zoom - oldZoom) < 0.0001)
        {
            return;
        }

        var position = e.GetPosition(ImageScrollHost);
        ImageScrollHost.UpdateLayout();

        var scale = vm.Zoom / oldZoom;
        var newOffsetX = (ImageScrollHost.HorizontalOffset + position.X) * scale - position.X;
        var newOffsetY = (ImageScrollHost.VerticalOffset + position.Y) * scale - position.Y;

        ImageScrollHost.ScrollToHorizontalOffset(newOffsetX);
        ImageScrollHost.ScrollToVerticalOffset(newOffsetY);
        e.Handled = true;
    }

    private void UpdateViewportSize()
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var width = ImageScrollHost.ViewportWidth > 0 ? ImageScrollHost.ViewportWidth : ImageScrollHost.ActualWidth;
        var height = ImageScrollHost.ViewportHeight > 0 ? ImageScrollHost.ViewportHeight : ImageScrollHost.ActualHeight;
        vm.UpdateViewportSize(width, height);
    }

    private void UpdateDisplayDpi()
    {
        if (PresentationSource.FromVisual(this) is { CompositionTarget.TransformToDevice: var matrix })
        {
            if (DataContext is MainViewModel vm)
            {
                vm.UpdateDisplayDpi(96.0 * matrix.M11, 96.0 * matrix.M22);
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
