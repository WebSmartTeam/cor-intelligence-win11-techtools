using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CORCleanup.Core;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;
using CORCleanup.Helpers;

namespace CORCleanup.ViewModels;

public partial class ToolsViewModel : ObservableObject
{
    private readonly IFileHashService _hashService;
    private readonly ISystemInfoService _systemInfo;
    private readonly IDiskAnalyserService _diskAnalyserService;
    private readonly IDuplicateFinderService _duplicateFinderService;
    private readonly IFileShredderService _fileShredderService;

    [ObservableProperty] private string _pageTitle = "Disk Tools";
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string _statusText = "Ready";

    // ================================================================
    // Disk Health
    // ================================================================

    [ObservableProperty] private bool _isLoadingDiskHealth;
    public ObservableCollection<DiskHealthInfo> Disks { get; } = new();

    // ================================================================
    // File Hash Checker
    // ================================================================

    [ObservableProperty] private string _hashFilePath = "";
    [ObservableProperty] private string _hashMd5 = "";
    [ObservableProperty] private string _hashSha1 = "";
    [ObservableProperty] private string _hashSha256 = "";
    [ObservableProperty] private string _hashFileSize = "";
    [ObservableProperty] private double _hashProgress;
    [ObservableProperty] private bool _isHashing;
    [ObservableProperty] private string _verifyHash = "";
    [ObservableProperty] private string _verifyResult = "";

    // ================================================================
    // Disk Analyser
    // ================================================================

    [ObservableProperty] private bool _isAnalysingDisk;
    [ObservableProperty] private string _diskAnalysePath = @"C:\";
    [ObservableProperty] private FolderSizeInfo? _currentFolderInfo;

    public ObservableCollection<FolderSizeInfo> FolderChildren { get; } = new();
    public ObservableCollection<LargeFileInfo> LargestFiles { get; } = new();

    private CancellationTokenSource? _diskAnalyseCts;

    // ================================================================
    // Duplicate Finder
    // ================================================================

    [ObservableProperty] private bool _isSearchingDuplicates;
    [ObservableProperty] private string _duplicateSearchPath = @"C:\Users";
    [ObservableProperty] private int _duplicateMinSizeKb = 1;
    [ObservableProperty] private int _duplicateGroupCount;
    [ObservableProperty] private string _duplicateTotalWasted = "";
    [ObservableProperty] private DuplicateGroup? _selectedDuplicateGroup;

    public ObservableCollection<DuplicateGroup> DuplicateGroups { get; } = new();

    private CancellationTokenSource? _duplicateCts;

    // ================================================================
    // File Shredder
    // ================================================================

    [ObservableProperty] private bool _isShredding;
    [ObservableProperty] private double _shredProgress;
    [ObservableProperty] private int _shredMethodIndex;
    [ObservableProperty] private string _wipeDriveLetter = "C";

    public ObservableCollection<string> ShredFiles { get; } = new();

    public static string[] ShredMethods => ["Zero Fill (1 pass)", "DoD 3-Pass", "Enhanced 7-Pass"];

    private CancellationTokenSource? _shredCts;

    // ================================================================
    // Constructor
    // ================================================================

    public ToolsViewModel(
        IFileHashService hashService,
        ISystemInfoService systemInfo,
        IDiskAnalyserService diskAnalyserService,
        IDuplicateFinderService duplicateFinderService,
        IFileShredderService fileShredderService)
    {
        _hashService = hashService;
        _systemInfo = systemInfo;
        _diskAnalyserService = diskAnalyserService;
        _duplicateFinderService = duplicateFinderService;
        _fileShredderService = fileShredderService;
    }

    // ================================================================
    // Disk Health Commands
    // ================================================================

    [RelayCommand]
    private async Task LoadDiskHealthAsync()
    {
        IsLoadingDiskHealth = true;
        StatusText = "Reading disk health...";

        try
        {
            var disks = await _systemInfo.GetDiskHealthAsync();
            Disks.Clear();
            foreach (var disk in disks)
                Disks.Add(disk);
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingDiskHealth = false;
            StatusText = $"{Disks.Count} disk(s) detected";
        }
    }

    // ================================================================
    // File Hash Checker Commands
    // ================================================================

    [RelayCommand]
    private async Task ComputeHashAsync()
    {
        if (string.IsNullOrWhiteSpace(HashFilePath)) return;

        IsHashing = true;
        HashMd5 = "";
        HashSha1 = "";
        HashSha256 = "";
        HashFileSize = "";
        VerifyResult = "";
        StatusText = "Computing hashes...";

        var progress = new Progress<double>(p => HashProgress = p);

        try
        {
            var result = await _hashService.ComputeHashesAsync(HashFilePath, progress);
            HashMd5 = result.Md5;
            HashSha1 = result.Sha1;
            HashSha256 = result.Sha256;
            HashFileSize = result.FileSizeFormatted;
            StatusText = $"Hashes computed for {result.FileName}";

            if (!string.IsNullOrWhiteSpace(VerifyHash))
                VerifyHashMatch();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsHashing = false;
            HashProgress = 0;
        }
    }

    [RelayCommand]
    private void VerifyHashMatch()
    {
        if (string.IsNullOrWhiteSpace(VerifyHash)) return;

        var clean = VerifyHash.Trim().ToLowerInvariant();
        if (clean == HashMd5)
            VerifyResult = "MATCH (MD5)";
        else if (clean == HashSha1)
            VerifyResult = "MATCH (SHA1)";
        else if (clean == HashSha256)
            VerifyResult = "MATCH (SHA256)";
        else
            VerifyResult = "NO MATCH — hash does not match any algorithm";
    }

    [RelayCommand]
    private void BrowseFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select file to hash",
            Filter = "All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            HashFilePath = dialog.FileName;
        }
    }

    // ================================================================
    // Disk Analyser Commands
    // ================================================================

    [RelayCommand]
    private async Task AnalyseDiskAsync()
    {
        if (string.IsNullOrWhiteSpace(DiskAnalysePath))
        {
            StatusText = "Please enter a folder path to analyse";
            return;
        }

        _diskAnalyseCts?.Cancel();
        _diskAnalyseCts = new CancellationTokenSource();
        var ct = _diskAnalyseCts.Token;

        IsAnalysingDisk = true;
        FolderChildren.Clear();
        LargestFiles.Clear();
        CurrentFolderInfo = null;
        StatusText = $"Analysing disk usage for {DiskAnalysePath}...";

        try
        {
            var folderTask = _diskAnalyserService.AnalyseFolderAsync(DiskAnalysePath, 3, ct);
            var largestTask = _diskAnalyserService.GetLargestFilesAsync(DiskAnalysePath, 50, ct);

            await Task.WhenAll(folderTask, largestTask);

            CurrentFolderInfo = await folderTask;

            foreach (var child in CurrentFolderInfo.Children.OrderByDescending(c => c.SizeBytes))
                FolderChildren.Add(child);

            foreach (var file in await largestTask)
                LargestFiles.Add(file);

            StatusText = $"Disk analysis complete — {CurrentFolderInfo.SizeFormatted} total, " +
                         $"{CurrentFolderInfo.FileCount:N0} files in {CurrentFolderInfo.FolderCount:N0} folders";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Disk analysis cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"Disk analysis error: {ex.Message}";
        }
        finally
        {
            IsAnalysingDisk = false;
        }
    }

    [RelayCommand]
    private void BrowseDiskFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select folder to analyse"
        };

        if (dialog.ShowDialog() == true)
            DiskAnalysePath = dialog.FolderName;
    }

    [RelayCommand]
    private void CancelDiskAnalysis()
    {
        _diskAnalyseCts?.Cancel();
    }

    // ================================================================
    // Duplicate Finder Commands
    // ================================================================

    [RelayCommand]
    private async Task SearchDuplicatesAsync()
    {
        if (string.IsNullOrWhiteSpace(DuplicateSearchPath))
        {
            StatusText = "Please enter a folder path to search for duplicates";
            return;
        }

        _duplicateCts?.Cancel();
        _duplicateCts = new CancellationTokenSource();
        var ct = _duplicateCts.Token;

        IsSearchingDuplicates = true;
        DuplicateGroups.Clear();
        DuplicateGroupCount = 0;
        DuplicateTotalWasted = "";
        SelectedDuplicateGroup = null;
        StatusText = $"Searching for duplicates in {DuplicateSearchPath}...";

        long totalWasted = 0;

        try
        {
            var minBytes = (long)DuplicateMinSizeKb * 1024;

            await foreach (var group in _duplicateFinderService.FindDuplicatesAsync(
                DuplicateSearchPath, minBytes, ct))
            {
                DuplicateGroups.Add(group);
                DuplicateGroupCount = DuplicateGroups.Count;
                totalWasted += group.WastedBytes;
                DuplicateTotalWasted = ByteFormatter.Format(totalWasted);
                StatusText = $"Found {DuplicateGroupCount} duplicate group(s) — {DuplicateTotalWasted} wasted";
            }

            StatusText = DuplicateGroupCount > 0
                ? $"Scan complete — {DuplicateGroupCount} duplicate group(s), {DuplicateTotalWasted} wasted space"
                : "Scan complete — no duplicates found";
        }
        catch (OperationCanceledException)
        {
            StatusText = $"Duplicate search stopped — {DuplicateGroupCount} group(s) found so far";
        }
        catch (Exception ex)
        {
            StatusText = $"Duplicate search error: {ex.Message}";
        }
        finally
        {
            IsSearchingDuplicates = false;
        }
    }

    [RelayCommand]
    private void StopDuplicateSearch()
    {
        _duplicateCts?.Cancel();
    }

    [RelayCommand]
    private void BrowseDuplicateFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select folder to search for duplicates"
        };

        if (dialog.ShowDialog() == true)
            DuplicateSearchPath = dialog.FolderName;
    }

    [RelayCommand]
    private async Task DeleteSelectedDuplicatesAsync()
    {
        var selectedFiles = DuplicateGroups
            .SelectMany(g => g.Files)
            .Where(f => f.IsSelected)
            .ToList();

        if (selectedFiles.Count == 0)
        {
            StatusText = "No duplicate files selected for deletion";
            return;
        }

        if (!DialogHelper.Confirm(
            $"Delete {selectedFiles.Count} selected duplicate file(s) to the Recycle Bin?\n\n" +
            "Files can be recovered from the Recycle Bin if needed.",
            "COR Cleanup — Delete Duplicates"))
            return;

        StatusText = $"Deleting {selectedFiles.Count} file(s)...";
        int deleted = 0;
        int failed = 0;

        foreach (var file in selectedFiles)
        {
            try
            {
                await _duplicateFinderService.DeleteToRecycleBinAsync(file.FullPath);
                deleted++;
            }
            catch
            {
                failed++;
            }
        }

        StatusText = failed == 0
            ? $"Deleted {deleted} file(s) to Recycle Bin"
            : $"Deleted {deleted} file(s), {failed} failed — check permissions";
    }

    // ================================================================
    // File Shredder Commands
    // ================================================================

    [RelayCommand]
    private void AddShredFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select files to securely shred",
            Filter = "All files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
            {
                if (!ShredFiles.Contains(file))
                    ShredFiles.Add(file);
            }

            StatusText = $"{ShredFiles.Count} file(s) queued for shredding";
        }
    }

    [RelayCommand]
    private void RemoveShredFile(string filePath)
    {
        ShredFiles.Remove(filePath);
        StatusText = $"{ShredFiles.Count} file(s) queued for shredding";
    }

    [RelayCommand]
    private void ClearShredFiles()
    {
        ShredFiles.Clear();
        StatusText = "Shred queue cleared";
    }

    [RelayCommand]
    private async Task ShredFilesExecuteAsync()
    {
        if (ShredFiles.Count == 0)
        {
            StatusText = "No files queued for shredding";
            return;
        }

        var method = (ShredMethod)ShredMethodIndex;
        var methodName = ShredMethods[ShredMethodIndex];

        if (!DialogHelper.Confirm(
            $"PERMANENTLY DESTROY {ShredFiles.Count} file(s) using {methodName}?\n\n" +
            "This operation CANNOT be undone. Files will be overwritten and permanently deleted.",
            "COR Cleanup — Secure Shred"))
            return;

        _shredCts?.Cancel();
        _shredCts = new CancellationTokenSource();
        var ct = _shredCts.Token;

        IsShredding = true;
        ShredProgress = 0;
        StatusText = $"Shredding {ShredFiles.Count} file(s) with {methodName}...";

        var progress = new Progress<double>(p => ShredProgress = p * 100);

        try
        {
            await _fileShredderService.ShredFilesAsync(ShredFiles.ToList(), method, progress, ct);
            StatusText = $"Shredding complete — {ShredFiles.Count} file(s) permanently destroyed";
            ShredFiles.Clear();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Shredding cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"Shredding error: {ex.Message}";
        }
        finally
        {
            IsShredding = false;
            ShredProgress = 0;
        }
    }

    [RelayCommand]
    private async Task WipeFreeSpaceAsync()
    {
        if (string.IsNullOrWhiteSpace(WipeDriveLetter))
        {
            StatusText = "Please enter a drive letter";
            return;
        }

        var drive = WipeDriveLetter.Trim().TrimEnd(':', '\\').ToUpperInvariant();
        var method = (ShredMethod)ShredMethodIndex;
        var methodName = ShredMethods[ShredMethodIndex];

        if (!DialogHelper.Confirm(
            $"Wipe ALL free space on drive {drive}:\\ using {methodName}?\n\n" +
            "This fills the free space with overwrite data, then deletes the temp file.\n" +
            "This can take a VERY LONG TIME depending on free space and method.\n\n" +
            "The drive will temporarily have almost zero free space during the operation.",
            "COR Cleanup — Wipe Free Space"))
            return;

        _shredCts?.Cancel();
        _shredCts = new CancellationTokenSource();
        var ct = _shredCts.Token;

        IsShredding = true;
        ShredProgress = 0;
        StatusText = $"Wiping free space on {drive}:\\ with {methodName}...";

        var progress = new Progress<double>(p => ShredProgress = p * 100);

        try
        {
            await _fileShredderService.WipeFreeSpaceAsync($@"{drive}:\", method, progress, ct);
            StatusText = $"Free space wipe complete on {drive}:\\";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Free space wipe cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"Free space wipe error: {ex.Message}";
        }
        finally
        {
            IsShredding = false;
            ShredProgress = 0;
        }
    }

    [RelayCommand]
    private void StopShred()
    {
        _shredCts?.Cancel();
    }
}
