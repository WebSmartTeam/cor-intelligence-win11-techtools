using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.ViewModels;

public partial class AutoToolViewModel : ObservableObject
{
    private readonly IAutoToolService _autoToolService;

    public AutoToolViewModel(IAutoToolService autoToolService)
    {
        _autoToolService = autoToolService;
    }

    // ================================================================
    // State Machine
    // ================================================================

    [ObservableProperty] private string _currentPhase = "Ready";
    [ObservableProperty] private bool _isDiagnosing;
    [ObservableProperty] private bool _isConsultingAi;
    [ObservableProperty] private bool _isExecuting;
    [ObservableProperty] private string _statusText = "Ready to run diagnostics";
    [ObservableProperty] private string _progressDetail = string.Empty;
    [ObservableProperty] private int _progressPercent;

    // Report
    [ObservableProperty] private bool _hasReport;
    [ObservableProperty] private string _reportSummary = string.Empty;
    [ObservableProperty] private string _machineName = string.Empty;
    [ObservableProperty] private string _osSummary = string.Empty;
    [ObservableProperty] private string _cpuSummary = string.Empty;
    [ObservableProperty] private string _ramSummary = string.Empty;
    [ObservableProperty] private string _cleanableSize = string.Empty;
    [ObservableProperty] private int _registryIssueCount;
    [ObservableProperty] private int _outdatedDriverCount;
    [ObservableProperty] private int _recentErrorCount;
    [ObservableProperty] private int _bloatwareCount;

    // AI
    [ObservableProperty] private bool _hasAiRecommendation;
    [ObservableProperty] private string _aiSummary = string.Empty;

    // Action selection
    [ObservableProperty] private int _selectedActionCount;

    // Execution results
    [ObservableProperty] private int _completedCount;
    [ObservableProperty] private int _failedCount;

    // Collections
    public ObservableCollection<AutoToolAction> Actions { get; } = new();

    // Report held in memory
    private DiagnosticReport? _currentReport;
    private CancellationTokenSource? _cts;

    // ================================================================
    // Phase 1: Run Diagnostics
    // ================================================================

    [RelayCommand]
    private async Task RunDiagnosticsAsync()
    {
        CurrentPhase = "Diagnosing";
        IsDiagnosing = true;
        StatusText = "Running full system diagnostic...";
        ProgressDetail = string.Empty;
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<string>(msg => ProgressDetail = msg);
            _currentReport = await _autoToolService.RunDiagnosticsAsync(progress, _cts.Token);

            // Populate report summary
            HasReport = true;
            MachineName = _currentReport.MachineName;
            OsSummary = $"{_currentReport.SystemInfo.OsEdition} ({_currentReport.SystemInfo.OsBuild})";
            CpuSummary = _currentReport.SystemInfo.CpuName;
            RamSummary = _currentReport.SystemInfo.TotalRamFormatted;
            CleanableSize = _currentReport.TotalCleanableFormatted;
            RegistryIssueCount = _currentReport.RegistryIssues.Count;
            OutdatedDriverCount = _currentReport.OutdatedDrivers.Count;
            RecentErrorCount = _currentReport.RecentErrors.Count;
            BloatwareCount = _currentReport.BloatwareApps.Count(b => b.IsInstalled);

            ReportSummary = $"{_currentReport.TotalIssueCount} issues found | " +
                            $"{CleanableSize} cleanable | " +
                            $"{_currentReport.MemoryInfo.MemoryLoadPercent}% RAM used";

            CurrentPhase = "ReportReady";
            StatusText = "Diagnostic complete";
        }
        catch (OperationCanceledException)
        {
            CurrentPhase = "Ready";
            StatusText = "Diagnostic cancelled";
        }
        catch (Exception ex)
        {
            CurrentPhase = "Ready";
            StatusText = $"Diagnostic failed: {ex.Message}";
        }
        finally
        {
            IsDiagnosing = false;
        }
    }

    // ================================================================
    // Phase 2a: Submit to AI
    // ================================================================

    [RelayCommand]
    private async Task SubmitToAiAsync()
    {
        if (_currentReport is null) return;

        CurrentPhase = "ConsultingAi";
        IsConsultingAi = true;
        StatusText = "Consulting COR Intelligence AI...";
        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        try
        {
            var recommendation = await _autoToolService.SubmitToAiAsync(_currentReport, _cts.Token);

            // Populate action catalogue
            PopulateActions();

            if (recommendation is not null)
            {
                HasAiRecommendation = true;
                AiSummary = recommendation.Summary;

                // Auto-tick AI-recommended actions
                foreach (var action in Actions)
                {
                    if (recommendation.RecommendedActionIds.Contains(action.ActionId))
                    {
                        action.IsSelected = true;
                        action.AiRecommended = true;
                        if (recommendation.ActionReasons.TryGetValue(action.ActionId, out var reason))
                            action.AiReason = reason;
                    }
                }

                StatusText = $"AI recommends {recommendation.RecommendedActionIds.Count} actions";
            }
            else
            {
                HasAiRecommendation = false;
                AiSummary = "AI consultation unavailable — select actions manually";
                StatusText = "AI unavailable — manual mode";
            }

            UpdateSelectedCount();
            CurrentPhase = "ActionSelection";
        }
        catch (OperationCanceledException)
        {
            // Timeout — fall through to manual selection
            PopulateActions();
            HasAiRecommendation = false;
            AiSummary = "AI consultation timed out — select actions manually";
            StatusText = "AI timed out — manual mode";
            CurrentPhase = "ActionSelection";
        }
        catch
        {
            PopulateActions();
            HasAiRecommendation = false;
            AiSummary = "AI consultation failed — select actions manually";
            StatusText = "AI failed — manual mode";
            CurrentPhase = "ActionSelection";
        }
        finally
        {
            IsConsultingAi = false;
        }
    }

    // ================================================================
    // Phase 2b: Skip AI (manual mode)
    // ================================================================

    [RelayCommand]
    private void SkipAi()
    {
        PopulateActions();
        HasAiRecommendation = false;
        AiSummary = string.Empty;
        StatusText = "Select actions to run";
        CurrentPhase = "ActionSelection";
    }

    // ================================================================
    // Phase 3: Execute Selected Actions
    // ================================================================

    [RelayCommand]
    private async Task ExecuteSelectedAsync()
    {
        if (_currentReport is null) return;

        var selected = Actions.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0) return;

        CurrentPhase = "Executing";
        IsExecuting = true;
        CompletedCount = 0;
        FailedCount = 0;
        _cts = new CancellationTokenSource();

        var total = selected.Count;
        var done = 0;

        foreach (var action in selected)
        {
            if (_cts.Token.IsCancellationRequested)
            {
                action.Status = ActionStatus.Skipped;
                action.ResultMessage = "Cancelled by user";
                continue;
            }

            action.Status = ActionStatus.Running;
            StatusText = $"Running: {action.DisplayName} ({done + 1}/{total})";
            ProgressPercent = (int)((double)done / total * 100);

            try
            {
                var progress = new Progress<string>(msg => ProgressDetail = msg);
                var result = await _autoToolService.ExecuteActionAsync(action, _currentReport, progress, _cts.Token);
                action.Status = ActionStatus.Completed;
                action.ResultMessage = result;
                CompletedCount++;
            }
            catch (OperationCanceledException)
            {
                action.Status = ActionStatus.Skipped;
                action.ResultMessage = "Cancelled";
            }
            catch (Exception ex)
            {
                action.Status = ActionStatus.Failed;
                action.ResultMessage = ex.Message;
                FailedCount++;
            }

            done++;
        }

        ProgressPercent = 100;
        IsExecuting = false;
        CurrentPhase = "Complete";
        StatusText = $"Complete — {CompletedCount} succeeded, {FailedCount} failed";
    }

    // ================================================================
    // Action Management
    // ================================================================

    [RelayCommand]
    private void SelectAllSafe()
    {
        foreach (var action in Actions)
        {
            action.IsSelected = action.RiskLevel is ActionRiskLevel.Safe or ActionRiskLevel.Low;
        }
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var action in Actions)
            action.IsSelected = false;
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void Reset()
    {
        Actions.Clear();
        _currentReport = null;
        HasReport = false;
        HasAiRecommendation = false;
        AiSummary = string.Empty;
        ProgressPercent = 0;
        ProgressDetail = string.Empty;
        CompletedCount = 0;
        FailedCount = 0;
        SelectedActionCount = 0;
        CurrentPhase = "Ready";
        StatusText = "Ready to run diagnostics";
    }

    // ================================================================
    // Helpers
    // ================================================================

    private void PopulateActions()
    {
        Actions.Clear();
        foreach (var action in _autoToolService.GetActionCatalogue())
        {
            action.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AutoToolAction.IsSelected))
                    UpdateSelectedCount();
            };
            Actions.Add(action);
        }
        UpdateSelectedCount();
    }

    private void UpdateSelectedCount()
    {
        SelectedActionCount = Actions.Count(a => a.IsSelected);
    }
}
