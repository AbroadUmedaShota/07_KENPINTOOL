using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using KenpinTool.Prototype.Models;
using KenpinTool.Prototype.Services;

namespace KenpinTool.Prototype.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly IImageLoaderService _imageLoaderService;

    public ObservableCollection<InspectionItem> Items { get; } = new();

    private InspectionItem? _selectedItem;
    public InspectionItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                if (value != null)
                {
                    _ = _imageLoaderService.EnqueueAsync(value.FilePath);
                }
            }
        }
    }

    private BitmapSource? _currentImage;
    public BitmapSource? CurrentImage
    {
        get => _currentImage;
        set => SetProperty(ref _currentImage, value);
    }

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public ICommand LoadFolderCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PrevCommand { get; }
    public ICommand ApproveCommand { get; }

    public MainViewModel(IImageLoaderService imageLoaderService)
    {
        _imageLoaderService = imageLoaderService;

        LoadFolderCommand = new AsyncRelayCommand<string>(LoadFolderAsync);
        NextCommand = new RelayCommand(Next);
        PrevCommand = new RelayCommand(Prev);
        ApproveCommand = new RelayCommand(Approve);

        _ = ListenForImagesAsync();
    }

    private async Task LoadFolderAsync(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sample-data");
            if (!Directory.Exists(path)) path = AppDomain.CurrentDomain.BaseDirectory;
        }

        StatusText = $"Loading {path}...";
        Items.Clear();

        if (!Directory.Exists(path))
        {
            StatusText = $"Directory not found: {path}";
            return;
        }

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".bmp", ".png" };
        var files = Directory.GetFiles(path, "*.*")
            .Where(s => allowedExtensions.Contains(Path.GetExtension(s).ToLower()))
            .OrderBy(s => s);

        int count = 1;
        foreach (var file in files)
        {
            Items.Add(new InspectionItem
            {
                FilePath = file,
                PageNumber = count++
            });
        }

        if (Items.Any())
        {
            SelectedItem = Items[0];
        }

        StatusText = $"{Items.Count} images loaded.";
    }

    private void Next()
    {
        if (SelectedItem == null) return;
        int index = Items.IndexOf(SelectedItem);
        if (index < Items.Count - 1)
        {
            SelectedItem = Items[index + 1];
        }
    }

    private void Prev()
    {
        if (SelectedItem == null) return;
        int index = Items.IndexOf(SelectedItem);
        if (index > 0)
        {
            SelectedItem = Items[index - 1];
        }
    }

    private void Approve()
    {
        if (SelectedItem != null)
        {
            SelectedItem.Status = "OK";
            Next();
        }
    }

    private async Task ListenForImagesAsync()
    {
        await foreach (var image in _imageLoaderService.GetImageStreamAsync())
        {
            CurrentImage = image;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(storage, value)) return false;
        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}