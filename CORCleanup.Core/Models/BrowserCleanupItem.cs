using System.ComponentModel;

namespace CORCleanup.Core.Models;

public sealed class BrowserCleanupItem : INotifyPropertyChanged
{
    public required string BrowserName { get; init; }
    public required string Category { get; init; }
    public required string Path { get; init; }
    public long SizeBytes { get; set; }
    public bool IsSafe { get; init; } = true;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SizeFormatted => ByteFormatter.Format(SizeBytes);
}
