using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace KenpinTool.Prototype.Models;

public class InspectionItem : INotifyPropertyChanged
{
    private string _filePath = string.Empty;
    public string FilePath
    {
        get => _filePath;
        set => SetProperty(ref _filePath, value);
    }

    private int _pageNumber;
    public int PageNumber
    {
        get => _pageNumber;
        set => SetProperty(ref _pageNumber, value);
    }

    private string _status = "Wait"; // Wait, OK, NG, Warn
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string FileName => Path.GetFileName(FilePath);

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