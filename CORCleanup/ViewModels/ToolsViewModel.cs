using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;

namespace CORCleanup.ViewModels;

public partial class ToolsViewModel : ObservableObject
{
    private readonly IFileHashService _hashService;
    private readonly IPasswordGeneratorService _passwordGenerator;
    private readonly IBsodViewerService _bsodViewerService;
    private readonly ISoftwareInventoryService _softwareInventoryService;
    private readonly IAntivirusService _antivirusService;

    [ObservableProperty] private string _pageTitle = "Utility Tools";
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string _statusText = "Ready";

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
    // Password Generator
    // ================================================================

    [ObservableProperty] private int _passwordLength = 16;
    [ObservableProperty] private bool _includeUppercase = true;
    [ObservableProperty] private bool _includeLowercase = true;
    [ObservableProperty] private bool _includeNumbers = true;
    [ObservableProperty] private bool _includeSymbols = true;
    [ObservableProperty] private bool _excludeAmbiguous;
    [ObservableProperty] private string _generatedPassword = "";

    // ================================================================
    // BSOD Crash History
    // ================================================================

    [ObservableProperty] private bool _isLoadingBsod;

    public ObservableCollection<BsodCrashEntry> BsodCrashes { get; } = new();

    // ================================================================
    // Software Inventory
    // ================================================================

    [ObservableProperty] private bool _isLoadingSoftware;
    [ObservableProperty] private string _softwareSearchFilter = "";
    [ObservableProperty] private bool _includeSystemComponents;

    public ObservableCollection<SoftwareEntry> SoftwareEntries { get; } = new();
    public ObservableCollection<SoftwareEntry> FilteredSoftware { get; } = new();

    // ================================================================
    // AV Health Scanner
    // ================================================================

    [ObservableProperty] private bool _isScanningAv;
    [ObservableProperty] private AntivirusProduct? _selectedAvProduct;

    public ObservableCollection<AntivirusProduct> AntivirusProducts { get; } = new();

    // ================================================================
    // Constructor
    // ================================================================

    public ToolsViewModel(
        IFileHashService hashService,
        IPasswordGeneratorService passwordGenerator,
        IBsodViewerService bsodViewerService,
        ISoftwareInventoryService softwareInventoryService,
        IAntivirusService antivirusService)
    {
        _hashService = hashService;
        _passwordGenerator = passwordGenerator;
        _bsodViewerService = bsodViewerService;
        _softwareInventoryService = softwareInventoryService;
        _antivirusService = antivirusService;
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
    // Password Generator Commands
    // ================================================================

    [RelayCommand]
    private void GeneratePassword()
    {
        try
        {
            GeneratedPassword = _passwordGenerator.Generate(new PasswordOptions
            {
                Length = PasswordLength,
                IncludeUppercase = IncludeUppercase,
                IncludeLowercase = IncludeLowercase,
                IncludeNumbers = IncludeNumbers,
                IncludeSymbols = IncludeSymbols,
                ExcludeAmbiguous = ExcludeAmbiguous
            });
            StatusText = $"Password generated ({PasswordLength} characters)";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CopyPassword()
    {
        if (!string.IsNullOrEmpty(GeneratedPassword))
        {
            System.Windows.Clipboard.SetText(GeneratedPassword);
            StatusText = "Password copied to clipboard";
        }
    }

    // ================================================================
    // BSOD Crash History Commands
    // ================================================================

    [RelayCommand]
    private async Task LoadBsodCrashesAsync()
    {
        IsLoadingBsod = true;
        BsodCrashes.Clear();
        StatusText = "Scanning for crash dumps...";

        try
        {
            var entries = await _bsodViewerService.GetCrashEntriesAsync();
            foreach (var entry in entries)
                BsodCrashes.Add(entry);

            StatusText = entries.Count > 0
                ? $"{entries.Count} crash dump(s) found — latest: {entries[0].CrashTimeFormatted}"
                : "No crash dumps found (good news!)";
        }
        catch (Exception ex)
        {
            StatusText = $"Error reading crash dumps: {ex.Message}";
        }
        finally
        {
            IsLoadingBsod = false;
        }
    }

    // ================================================================
    // Software Inventory Commands
    // ================================================================

    [RelayCommand]
    private async Task LoadSoftwareAsync()
    {
        IsLoadingSoftware = true;
        SoftwareEntries.Clear();
        FilteredSoftware.Clear();
        StatusText = "Loading installed software...";

        try
        {
            var entries = await _softwareInventoryService.GetInstalledSoftwareAsync(IncludeSystemComponents);
            foreach (var entry in entries)
                SoftwareEntries.Add(entry);

            ApplySoftwareFilter();
            StatusText = $"{SoftwareEntries.Count} program(s) found";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoadingSoftware = false;
        }
    }

    partial void OnSoftwareSearchFilterChanged(string value) => ApplySoftwareFilter();

    private void ApplySoftwareFilter()
    {
        FilteredSoftware.Clear();

        var filter = SoftwareSearchFilter?.Trim() ?? "";
        var source = string.IsNullOrEmpty(filter)
            ? SoftwareEntries
            : SoftwareEntries.Where(e =>
                e.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (e.Publisher?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false));

        foreach (var entry in source)
            FilteredSoftware.Add(entry);
    }

    [RelayCommand]
    private async Task ExportSoftwareCsvAsync()
    {
        if (FilteredSoftware.Count == 0) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Software Inventory",
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"Software_Inventory_{DateTime.Now:yyyyMMdd}.csv"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                await _softwareInventoryService.ExportToCsvAsync(FilteredSoftware, dialog.FileName);
                StatusText = $"Exported {FilteredSoftware.Count} entries to {System.IO.Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                StatusText = $"Export error: {ex.Message}";
            }
        }
    }

    // ================================================================
    // AV Health Scanner Commands
    // ================================================================

    [RelayCommand]
    private async Task ScanAntivirusAsync()
    {
        IsScanningAv = true;
        AntivirusProducts.Clear();
        SelectedAvProduct = null;
        StatusText = "Scanning for antivirus products...";

        var progress = new Progress<string>(msg => StatusText = msg);

        try
        {
            var products = await _antivirusService.ScanAsync(progress);
            foreach (var product in products)
                AntivirusProducts.Add(product);

            var active = products.Count(p => p.Status == AntivirusStatus.Active);
            var remnants = products.Count(p => p.Status == AntivirusStatus.Remnant);
            var conflicts = products.Count(p => p.Status == AntivirusStatus.Conflict);

            var summary = $"{products.Count} product(s) found";
            if (active > 0) summary += $" — {active} active";
            if (remnants > 0) summary += $", {remnants} remnant(s)";
            if (conflicts > 0) summary += $", {conflicts} CONFLICT(S)";

            StatusText = products.Count == 0
                ? "No antivirus products detected (Windows Defender may be the sole provider)"
                : summary;
        }
        catch (Exception ex)
        {
            StatusText = $"AV scan error: {ex.Message}";
        }
        finally
        {
            IsScanningAv = false;
        }
    }
}
