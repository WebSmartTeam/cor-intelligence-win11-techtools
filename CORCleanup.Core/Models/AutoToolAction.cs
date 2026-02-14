using System.ComponentModel;

namespace CORCleanup.Core.Models;

public enum ActionRiskLevel
{
    Safe,
    Low,
    Medium,
    High
}

public enum ActionStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped
}

/// <summary>
/// A single remediation action in the Auto Tool catalogue.
/// Implements INotifyPropertyChanged for live UI binding during execution.
/// </summary>
public sealed class AutoToolAction : INotifyPropertyChanged
{
    public required string ActionId { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required ActionRiskLevel RiskLevel { get; init; }

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

    private ActionStatus _status = ActionStatus.Pending;
    public ActionStatus Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusDisplay)));
        }
    }

    private string? _resultMessage;
    public string? ResultMessage
    {
        get => _resultMessage;
        set
        {
            if (_resultMessage == value) return;
            _resultMessage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResultMessage)));
        }
    }

    private bool _aiRecommended;
    public bool AiRecommended
    {
        get => _aiRecommended;
        set
        {
            if (_aiRecommended == value) return;
            _aiRecommended = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AiRecommended)));
        }
    }

    private string? _aiReason;
    public string? AiReason
    {
        get => _aiReason;
        set
        {
            if (_aiReason == value) return;
            _aiReason = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AiReason)));
        }
    }

    // Computed
    public string RiskDisplay => RiskLevel.ToString();
    public string StatusDisplay => Status.ToString();

    public event PropertyChangedEventHandler? PropertyChanged;
}
