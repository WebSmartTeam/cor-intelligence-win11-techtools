using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;
using CORCleanup.Core.Services.Network;
using CORCleanup.Helpers;

namespace CORCleanup.ViewModels;

public partial class NetworkViewModel : ObservableObject
{
    private readonly IPingService _pingService;
    private readonly ITracerouteService _tracerouteService;
    private readonly IDnsLookupService _dnsLookupService;
    private readonly IPortScannerService _portScannerService;
    private readonly INetworkInfoService _networkInfoService;
    private readonly ISubnetCalculatorService _subnetCalculatorService;
    private readonly IWifiService _wifiService;
    private readonly IWifiScannerService _wifiScannerService;
    private readonly ISpeedTestService _speedTestService;

    private CancellationTokenSource? _pingCts;
    private CancellationTokenSource? _traceCts;
    private CancellationTokenSource? _portScanCts;
    private CancellationTokenSource? _signalCts;
    private CancellationTokenSource? _speedCts;

    [ObservableProperty] private string _pageTitle = "Network Tools";
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string _statusText = "Ready";

    // ================================================================
    // Continuous Ping
    // ================================================================

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartPingCommand))]
    private string _pingTarget = "8.8.8.8";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartPingCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopPingCommand))]
    private bool _isPinging;

    [ObservableProperty] private int _totalSent;
    [ObservableProperty] private int _totalReceived;
    [ObservableProperty] private int _totalLost;
    [ObservableProperty] private double _lossPercentage;
    [ObservableProperty] private long _minMs;
    [ObservableProperty] private long _maxMs;
    [ObservableProperty] private double _avgMs;

    public ObservableCollection<PingResult> PingResults { get; } = new();

    // ================================================================
    // Traceroute
    // ================================================================

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTraceCommand))]
    private string _traceTarget = "8.8.8.8";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTraceCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopTraceCommand))]
    private bool _isTracing;

    [ObservableProperty] private string _traceStatus = "";

    public ObservableCollection<TracerouteHop> TraceHops { get; } = new();

    // ================================================================
    // DNS Lookup
    // ================================================================

    [ObservableProperty] private string _dnsDomain = "";
    [ObservableProperty] private int _dnsRecordTypeIndex;
    [ObservableProperty] private int _dnsServerIndex;
    [ObservableProperty] private string _dnsCustomServer = "";
    [ObservableProperty] private bool _isDnsLooking;
    [ObservableProperty] private string _dnsStatus = "";

    public ObservableCollection<DnsRecord> DnsRecords { get; } = new();

    public static string[] DnsRecordTypes => ["A", "AAAA", "MX", "CNAME", "TXT", "NS", "SOA"];
    public static string[] DnsServerOptions => ["System Default", "Google (8.8.8.8)", "Cloudflare (1.1.1.1)", "Quad9 (9.9.9.9)", "OpenDNS (208.67.222.222)", "Custom"];

    // ================================================================
    // Port Scanner
    // ================================================================

    [ObservableProperty] private string _portScanHost = "";
    [ObservableProperty] private string _portScanRange = "80,443,22,3389,445,135,139,21,25,53";
    [ObservableProperty] private int _portPresetIndex;
    [ObservableProperty] private bool _isPortScanning;
    [ObservableProperty] private string _portScanStatus = "";
    [ObservableProperty] private int _portScanTabIndex;

    public ObservableCollection<PortScanResult> PortScanResults { get; } = new();
    public ObservableCollection<LocalPortEntry> LocalPorts { get; } = new();

    public static string[] PortPresets => ["Custom", "Common (Top 20)", "Web Server", "Remote Access", "Email", "Database"];

    // ================================================================
    // Quick Network Info
    // ================================================================

    [ObservableProperty] private bool _isLoadingNetInfo;
    [ObservableProperty] private string _publicIp = "—";

    public ObservableCollection<NetworkAdapterInfo> Adapters { get; } = new();

    // ================================================================
    // Subnet Calculator
    // ================================================================

    [ObservableProperty] private string _subnetInput = "192.168.1.0/24";
    [ObservableProperty] private string _subnetIp1 = "";
    [ObservableProperty] private string _subnetIp2 = "";
    [ObservableProperty] private string _subnetMaskInput = "255.255.255.0";
    [ObservableProperty] private SubnetCalculation? _subnetResult;
    [ObservableProperty] private string _subnetError = "";
    [ObservableProperty] private string _sameSubnetResult = "";

    // ================================================================
    // Wi-Fi Scanner
    // ================================================================

    [ObservableProperty] private bool _isScanningWifi;
    [ObservableProperty] private string _wifiScanStatus = "";
    [ObservableProperty] private int _wifiSubTabIndex;

    public ObservableCollection<WifiNetwork> WifiNetworks { get; } = new();
    public ObservableCollection<ChannelUsageInfo> ChannelUsage { get; } = new();

    // ================================================================
    // Wi-Fi Signal Monitor
    // ================================================================

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartSignalMonitorCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopSignalMonitorCommand))]
    private bool _isMonitoringSignal;

    [ObservableProperty] private string _monitorSsid = "—";
    [ObservableProperty] private int _monitorSignal;
    [ObservableProperty] private string _monitorChannel = "—";
    [ObservableProperty] private string _monitorLinkSpeed = "—";

    public ObservableCollection<WifiSignalReading> SignalHistory { get; } = new();

    // ================================================================
    // Wi-Fi Saved Profiles
    // ================================================================

    [ObservableProperty] private bool _isLoadingWifi;

    public ObservableCollection<WifiProfile> WifiProfiles { get; } = new();

    // ================================================================
    // Speed Test
    // ================================================================

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunSpeedTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelSpeedTestCommand))]
    private bool _isTestingSpeed;

    [ObservableProperty] private string _speedTestDownload = "—";
    [ObservableProperty] private string _speedTestUpload = "—";
    [ObservableProperty] private string _speedTestLatency = "—";
    [ObservableProperty] private string _speedTestJitter = "—";
    [ObservableProperty] private string _speedTestServer = "—";
    [ObservableProperty] private string _speedTestProgress = "";
    [ObservableProperty] private int _speedTestPercent;
    [ObservableProperty] private bool _hasSpeedResult;

    // ================================================================
    // Network Scanner (Advanced IP Scanner style)
    // ================================================================

    private readonly INetworkScannerService _networkScannerService;
    private CancellationTokenSource? _scanCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopScanCommand))]
    private bool _isScanning;

    [ObservableProperty] private string _scanSubnet = "192.168.1.0/24";
    [ObservableProperty] private string _scanStatus = "";
    [ObservableProperty] private int _scannedCount;
    [ObservableProperty] private int _onlineCount;
    [ObservableProperty] private NetworkDevice? _selectedDevice;
    [ObservableProperty] private bool _showOnlineOnly;

    public ObservableCollection<NetworkDevice> ScannedDevices { get; } = new();
    public ObservableCollection<NetworkDevice> FilteredDevices { get; } = new();
    public ObservableCollection<NetworkAdapterInfo> ScanAdapters { get; } = new();

    // ================================================================
    // Active Connections (TCPView style)
    // ================================================================

    private readonly IConnectionMonitorService _connectionMonitorService;

    [ObservableProperty] private bool _isLoadingConnections;
    [ObservableProperty] private string _connectionFilter = "";
    [ObservableProperty] private ConnectionEntry? _selectedConnection;

    public ObservableCollection<ConnectionEntry> Connections { get; } = new();
    public ObservableCollection<ConnectionEntry> FilteredConnections { get; } = new();

    // ================================================================
    // Constructor
    // ================================================================

    public NetworkViewModel(
        IPingService pingService,
        ITracerouteService tracerouteService,
        IDnsLookupService dnsLookupService,
        IPortScannerService portScannerService,
        INetworkInfoService networkInfoService,
        ISubnetCalculatorService subnetCalculatorService,
        IWifiService wifiService,
        IWifiScannerService wifiScannerService,
        INetworkScannerService networkScannerService,
        IConnectionMonitorService connectionMonitorService,
        ISpeedTestService speedTestService)
    {
        _pingService = pingService;
        _tracerouteService = tracerouteService;
        _dnsLookupService = dnsLookupService;
        _portScannerService = portScannerService;
        _networkInfoService = networkInfoService;
        _subnetCalculatorService = subnetCalculatorService;
        _wifiService = wifiService;
        _wifiScannerService = wifiScannerService;
        _networkScannerService = networkScannerService;
        _connectionMonitorService = connectionMonitorService;
        _speedTestService = speedTestService;
    }

    // ================================================================
    // Ping Commands
    // ================================================================

    private bool CanStartPing() => !IsPinging && !string.IsNullOrWhiteSpace(PingTarget);
    private bool CanStopPing() => IsPinging;

    [RelayCommand(CanExecute = nameof(CanStartPing))]
    private async Task StartPingAsync()
    {
        _pingCts = new CancellationTokenSource();
        IsPinging = true;
        PingResults.Clear();
        ResetPingStatistics();
        StatusText = $"Pinging {PingTarget}...";

        try
        {
            await foreach (var result in _pingService.ContinuousPingAsync(
                PingTarget, 1000, _pingCts.Token))
            {
                PingResults.Insert(0, result);
                UpdatePingStatistics(result);

                if (PingResults.Count > 500)
                    PingResults.RemoveAt(PingResults.Count - 1);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsPinging = false;
            StatusText = $"Stopped — {TotalSent} sent, {TotalReceived} received, {LossPercentage:F1}% loss";
            _pingCts?.Dispose();
            _pingCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopPing))]
    private void StopPing() => _pingCts?.Cancel();

    private void ResetPingStatistics()
    {
        TotalSent = 0; TotalReceived = 0; TotalLost = 0;
        LossPercentage = 0; MinMs = 0; MaxMs = 0; AvgMs = 0;
    }

    private void UpdatePingStatistics(PingResult result)
    {
        TotalSent++;
        if (result.IsSuccess)
        {
            TotalReceived++;
            if (TotalReceived == 1)
            {
                MinMs = result.RoundtripMs; MaxMs = result.RoundtripMs; AvgMs = result.RoundtripMs;
            }
            else
            {
                if (result.RoundtripMs < MinMs) MinMs = result.RoundtripMs;
                if (result.RoundtripMs > MaxMs) MaxMs = result.RoundtripMs;
                AvgMs = ((AvgMs * (TotalReceived - 1)) + result.RoundtripMs) / TotalReceived;
            }
        }
        else { TotalLost++; }
        LossPercentage = TotalSent > 0 ? (double)TotalLost / TotalSent * 100 : 0;
    }

    // ================================================================
    // Traceroute Commands
    // ================================================================

    private bool CanStartTrace() => !IsTracing && !string.IsNullOrWhiteSpace(TraceTarget);
    private bool CanStopTrace() => IsTracing;

    [RelayCommand(CanExecute = nameof(CanStartTrace))]
    private async Task StartTraceAsync()
    {
        _traceCts = new CancellationTokenSource();
        IsTracing = true;
        TraceHops.Clear();
        TraceStatus = $"Tracing route to {TraceTarget}...";
        StatusText = TraceStatus;

        try
        {
            await foreach (var hop in _tracerouteService.TraceAsync(
                TraceTarget, 30, 3000, _traceCts.Token))
            {
                TraceHops.Add(hop);
                TraceStatus = $"Hop {hop.HopNumber}: {hop.DisplayAddress}";
            }

            TraceStatus = $"Trace complete — {TraceHops.Count} hop(s) to {TraceTarget}";
            StatusText = TraceStatus;
        }
        catch (OperationCanceledException)
        {
            TraceStatus = "Trace cancelled";
            StatusText = TraceStatus;
        }
        catch (Exception ex)
        {
            TraceStatus = $"Trace error: {ex.Message}";
            StatusText = TraceStatus;
        }
        finally
        {
            IsTracing = false;
            _traceCts?.Dispose();
            _traceCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopTrace))]
    private void StopTrace() => _traceCts?.Cancel();

    // ================================================================
    // DNS Lookup Commands
    // ================================================================

    [RelayCommand]
    private async Task DnsLookupAsync()
    {
        if (string.IsNullOrWhiteSpace(DnsDomain)) return;

        IsDnsLooking = true;
        DnsRecords.Clear();
        DnsStatus = $"Looking up {DnsDomain}...";
        StatusText = DnsStatus;

        try
        {
            var recordType = DnsRecordTypes[DnsRecordTypeIndex];

            // Resolve DNS server from selection
            string? dnsServer = DnsServerIndex switch
            {
                0 => null,                    // System Default
                1 => "8.8.8.8",              // Google
                2 => "1.1.1.1",              // Cloudflare
                3 => "9.9.9.9",              // Quad9
                4 => "208.67.222.222",       // OpenDNS
                5 => string.IsNullOrWhiteSpace(DnsCustomServer) ? null : DnsCustomServer,
                _ => null
            };

            var result = await _dnsLookupService.LookupAsync(DnsDomain, recordType, dnsServer);

            foreach (var record in result.Records)
                DnsRecords.Add(record);

            DnsStatus = result.IsSuccess
                ? $"{result.Records.Count} record(s) found in {result.QueryTimeMs}ms via {result.DnsServer}"
                : $"Error: {result.Error}";
            StatusText = DnsStatus;
        }
        catch (Exception ex)
        {
            DnsStatus = $"DNS error: {ex.Message}";
            StatusText = DnsStatus;
        }
        finally
        {
            IsDnsLooking = false;
        }
    }

    [RelayCommand]
    private async Task DnsPropagationCheckAsync()
    {
        if (string.IsNullOrWhiteSpace(DnsDomain)) return;

        IsDnsLooking = true;
        DnsRecords.Clear();
        DnsStatus = $"Checking propagation for {DnsDomain}...";
        StatusText = DnsStatus;

        try
        {
            var recordType = DnsRecordTypes[DnsRecordTypeIndex];
            var results = await _dnsLookupService.PropagationCheckAsync(DnsDomain, recordType);

            foreach (var result in results)
            {
                foreach (var record in result.Records)
                {
                    DnsRecords.Add(new DnsRecord
                    {
                        Type = record.Type,
                        Name = $"[{result.DnsServer}] {record.Name}",
                        Value = record.Value,
                        Ttl = record.Ttl,
                        Priority = record.Priority
                    });
                }

                if (!result.IsSuccess)
                {
                    DnsRecords.Add(new DnsRecord
                    {
                        Type = "ERR",
                        Name = $"[{result.DnsServer}]",
                        Value = result.Error ?? "No records"
                    });
                }
            }

            DnsStatus = $"Propagation check complete — {results.Count} servers queried";
            StatusText = DnsStatus;
        }
        catch (Exception ex)
        {
            DnsStatus = $"Propagation error: {ex.Message}";
            StatusText = DnsStatus;
        }
        finally
        {
            IsDnsLooking = false;
        }
    }

    // ================================================================
    // Port Scanner Commands
    // ================================================================

    [RelayCommand]
    private async Task StartPortScanAsync()
    {
        if (string.IsNullOrWhiteSpace(PortScanHost)) return;

        _portScanCts?.Cancel();
        _portScanCts = new CancellationTokenSource();

        IsPortScanning = true;
        PortScanResults.Clear();

        // Resolve ports from preset or custom range
        var ports = GetPortsFromSelection();
        PortScanStatus = $"Scanning {ports.Length} port(s) on {PortScanHost}...";
        StatusText = PortScanStatus;

        try
        {
            int scanned = 0;
            int open = 0;

            await foreach (var result in _portScannerService.ScanPortsAsync(
                PortScanHost, ports, 2000, _portScanCts.Token))
            {
                PortScanResults.Add(result);
                scanned++;
                if (result.IsOpen) open++;
                PortScanStatus = $"Scanned {scanned}/{ports.Length} — {open} open";
            }

            PortScanStatus = $"Scan complete: {open} open of {ports.Length} port(s) on {PortScanHost}";
            StatusText = PortScanStatus;
        }
        catch (OperationCanceledException)
        {
            PortScanStatus = "Port scan cancelled";
            StatusText = PortScanStatus;
        }
        catch (Exception ex)
        {
            PortScanStatus = $"Scan error: {ex.Message}";
            StatusText = PortScanStatus;
        }
        finally
        {
            IsPortScanning = false;
            _portScanCts?.Dispose();
            _portScanCts = null;
        }
    }

    [RelayCommand]
    private void StopPortScan() => _portScanCts?.Cancel();

    [RelayCommand]
    private async Task LoadLocalPortsAsync()
    {
        IsPortScanning = true;
        LocalPorts.Clear();
        PortScanStatus = "Loading local ports...";

        try
        {
            var entries = await _portScannerService.GetLocalPortsAsync();
            foreach (var entry in entries)
                LocalPorts.Add(entry);

            PortScanStatus = $"{entries.Count} local port(s) found";
            StatusText = PortScanStatus;
        }
        catch (Exception ex)
        {
            PortScanStatus = $"Error: {ex.Message}";
            StatusText = PortScanStatus;
        }
        finally
        {
            IsPortScanning = false;
        }
    }

    private int[] GetPortsFromSelection()
    {
        return PortPresetIndex switch
        {
            1 => PortScannerService.Presets.CommonAll,
            2 => PortScannerService.Presets.WebServer,
            3 => PortScannerService.Presets.RemoteAccess,
            4 => PortScannerService.Presets.Email,
            5 => PortScannerService.Presets.Database,
            _ => ParsePortRange(PortScanRange)
        };
    }

    private static int[] ParsePortRange(string input)
    {
        var ports = new HashSet<int>();
        foreach (var part in input.Split(',', StringSplitOptions.TrimEntries))
        {
            if (part.Contains('-'))
            {
                var range = part.Split('-');
                if (int.TryParse(range[0], out var start) && int.TryParse(range[1], out var end))
                {
                    for (int p = Math.Max(1, start); p <= Math.Min(65535, end); p++)
                        ports.Add(p);
                }
            }
            else if (int.TryParse(part, out var port) && port >= 1 && port <= 65535)
            {
                ports.Add(port);
            }
        }
        return ports.OrderBy(p => p).ToArray();
    }

    // ================================================================
    // Network Info Commands
    // ================================================================

    [RelayCommand]
    private async Task LoadNetworkInfoAsync()
    {
        IsLoadingNetInfo = true;
        Adapters.Clear();
        PublicIp = "Loading...";
        StatusText = "Loading network information...";

        try
        {
            // Load adapters and public IP in parallel
            var adaptersTask = _networkInfoService.GetAdaptersAsync();
            var publicIpTask = _networkInfoService.GetPublicIpAsync();

            await Task.WhenAll(adaptersTask, publicIpTask);

            foreach (var adapter in adaptersTask.Result)
                Adapters.Add(adapter);

            PublicIp = publicIpTask.Result ?? "Unable to determine";
            StatusText = $"{Adapters.Count} adapter(s) found — Public IP: {PublicIp}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            PublicIp = "Error";
        }
        finally
        {
            IsLoadingNetInfo = false;
        }
    }

    // ================================================================
    // Subnet Calculator Commands
    // ================================================================

    [RelayCommand]
    private void CalculateSubnet()
    {
        SubnetError = "";
        SubnetResult = null;

        if (string.IsNullOrWhiteSpace(SubnetInput)) return;

        try
        {
            SubnetResult = _subnetCalculatorService.Calculate(SubnetInput);
            StatusText = $"Subnet: {SubnetResult.NetworkAddress}/{SubnetResult.CidrNotation} — {SubnetResult.UsableHosts:N0} usable hosts";
        }
        catch (ArgumentException ex)
        {
            SubnetError = ex.Message;
            StatusText = $"Subnet error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CheckSameSubnet()
    {
        SameSubnetResult = "";

        if (string.IsNullOrWhiteSpace(SubnetIp1) ||
            string.IsNullOrWhiteSpace(SubnetIp2) ||
            string.IsNullOrWhiteSpace(SubnetMaskInput))
        {
            SameSubnetResult = "Enter two IPs and a subnet mask";
            return;
        }

        try
        {
            var same = _subnetCalculatorService.AreOnSameSubnet(SubnetIp1, SubnetIp2, SubnetMaskInput);
            SameSubnetResult = same
                ? $"{SubnetIp1} and {SubnetIp2} ARE on the same subnet"
                : $"{SubnetIp1} and {SubnetIp2} are NOT on the same subnet";
        }
        catch (ArgumentException ex)
        {
            SameSubnetResult = ex.Message;
        }
    }

    // ================================================================
    // Wi-Fi Scanner Commands
    // ================================================================

    [RelayCommand]
    private async Task ScanWifiAsync()
    {
        IsScanningWifi = true;
        WifiNetworks.Clear();
        ChannelUsage.Clear();
        WifiScanStatus = "Scanning for nearby networks...";
        StatusText = WifiScanStatus;

        try
        {
            var networks = await _wifiScannerService.ScanNetworksAsync();

            foreach (var network in networks)
                WifiNetworks.Add(network);

            var channelUsage = _wifiScannerService.GetChannelUsage(networks);
            foreach (var usage in channelUsage)
                ChannelUsage.Add(usage);

            var bands = networks.Select(n => n.Band).Distinct().ToList();
            WifiScanStatus = $"{networks.Count} AP(s) found across {channelUsage.Count} channel(s) — {string.Join(", ", bands)}";
            StatusText = WifiScanStatus;
        }
        catch (Exception ex)
        {
            WifiScanStatus = $"Scan error: {ex.Message}";
            StatusText = WifiScanStatus;
        }
        finally
        {
            IsScanningWifi = false;
        }
    }

    // ================================================================
    // Wi-Fi Signal Monitor Commands
    // ================================================================

    private bool CanStartSignalMonitor() => !IsMonitoringSignal;
    private bool CanStopSignalMonitor() => IsMonitoringSignal;

    [RelayCommand(CanExecute = nameof(CanStartSignalMonitor))]
    private async Task StartSignalMonitorAsync()
    {
        _signalCts = new CancellationTokenSource();
        IsMonitoringSignal = true;
        SignalHistory.Clear();
        MonitorSsid = "Connecting...";
        MonitorSignal = 0;
        MonitorChannel = "—";
        MonitorLinkSpeed = "—";
        StatusText = "Monitoring Wi-Fi signal...";

        try
        {
            await foreach (var reading in _wifiScannerService.MonitorSignalAsync(1000, _signalCts.Token))
            {
                MonitorSsid = reading.Ssid ?? "—";
                MonitorSignal = reading.SignalPercent;
                MonitorChannel = reading.Channel?.ToString() ?? "—";
                MonitorLinkSpeed = reading.LinkSpeedMbps is not null
                    ? $"{reading.LinkSpeedMbps} Mbps"
                    : "—";

                SignalHistory.Add(reading);

                // Keep last 120 readings (2 minutes at 1s interval)
                while (SignalHistory.Count > 120)
                    SignalHistory.RemoveAt(0);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsMonitoringSignal = false;
            StatusText = $"Signal monitor stopped — {SignalHistory.Count} reading(s) captured";
            _signalCts?.Dispose();
            _signalCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopSignalMonitor))]
    private void StopSignalMonitor() => _signalCts?.Cancel();

    // ================================================================
    // Wi-Fi Saved Profile Commands
    // ================================================================

    [RelayCommand]
    private async Task LoadWifiProfilesAsync()
    {
        IsLoadingWifi = true;
        WifiProfiles.Clear();
        StatusText = "Loading Wi-Fi profiles...";

        try
        {
            var profiles = await _wifiService.GetSavedProfilesAsync();
            foreach (var profile in profiles)
                WifiProfiles.Add(profile);

            StatusText = $"{profiles.Count} Wi-Fi profile(s) found";
        }
        catch (Exception ex)
        {
            StatusText = $"Wi-Fi error: {ex.Message}";
        }
        finally
        {
            IsLoadingWifi = false;
        }
    }

    // ================================================================
    // Network Scanner Commands
    // ================================================================

    private bool CanStartScan() => !IsScanning;
    private bool CanStopScan() => IsScanning;

    [RelayCommand]
    private async Task LoadScanAdaptersAsync()
    {
        ScanAdapters.Clear();
        ScanStatus = "Detecting network adapters...";

        try
        {
            var adapters = await _networkScannerService.GetAdaptersWithSubnetsAsync();

            foreach (var adapter in adapters)
                ScanAdapters.Add(adapter);

            // Auto-select the first active adapter's subnet
            var active = adapters.FirstOrDefault(a =>
                a.Status == "Up" && !string.IsNullOrEmpty(a.IpAddress) && !string.IsNullOrEmpty(a.SubnetMask));

            if (active is not null)
            {
                var cidr = SubnetMaskToCidr(active.SubnetMask!);
                var baseIp = CalculateNetworkAddress(active.IpAddress!, active.SubnetMask!);
                ScanSubnet = $"{baseIp}/{cidr}";
            }

            ScanStatus = $"{adapters.Count} adapter(s) detected";
        }
        catch (Exception ex)
        {
            ScanStatus = $"Adapter detection error: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private async Task StartScanAsync()
    {
        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        ScannedDevices.Clear();
        FilteredDevices.Clear();
        ScannedCount = 0;
        OnlineCount = 0;

        // Parse subnet string (e.g. "192.168.1.0/24")
        var parts = ScanSubnet.Split('/');
        var baseIp = parts[0].Trim();
        var cidr = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var c) ? c : 24;

        ScanStatus = $"Scanning {baseIp}/{cidr}...";
        StatusText = ScanStatus;

        try
        {
            await foreach (var device in _networkScannerService.ScanSubnetAsync(baseIp, cidr, _scanCts.Token))
            {
                ScannedDevices.Add(device);
                ScannedCount++;
                if (device.IsOnline) OnlineCount++;
                ApplyScanFilter();

                ScanStatus = $"Scanned {ScannedCount} — {OnlineCount} online";
            }

            ScanStatus = $"Scan complete: {ScannedCount} host(s) scanned, {OnlineCount} online";
            StatusText = ScanStatus;
        }
        catch (OperationCanceledException)
        {
            ScanStatus = $"Scan stopped: {ScannedCount} scanned, {OnlineCount} online";
            StatusText = ScanStatus;
        }
        catch (Exception ex)
        {
            ScanStatus = $"Scan error: {ex.Message}";
            StatusText = ScanStatus;
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopScan))]
    private void StopScan() => _scanCts?.Cancel();

    partial void OnShowOnlineOnlyChanged(bool value) => ApplyScanFilter();

    private void ApplyScanFilter()
    {
        FilteredDevices.Clear();
        var source = ShowOnlineOnly
            ? ScannedDevices.Where(d => d.IsOnline)
            : ScannedDevices;

        foreach (var device in source)
            FilteredDevices.Add(device);
    }

    [RelayCommand]
    private void CopyDeviceIp()
    {
        if (SelectedDevice is null) return;

        try
        {
            System.Windows.Clipboard.SetText(SelectedDevice.IpAddress);
            StatusText = $"Copied {SelectedDevice.IpAddress} to clipboard";
        }
        catch (Exception ex)
        {
            StatusText = $"Copy failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Converts a dotted-decimal subnet mask (e.g. "255.255.255.0") to CIDR prefix length (e.g. 24).
    /// </summary>
    private static int SubnetMaskToCidr(string subnetMask)
    {
        if (!IPAddress.TryParse(subnetMask, out var mask))
            return 24;

        var bytes = mask.GetAddressBytes();
        int cidr = 0;
        foreach (var b in bytes)
        {
            cidr += b switch
            {
                255 => 8,
                254 => 7,
                252 => 6,
                248 => 5,
                240 => 4,
                224 => 3,
                192 => 2,
                128 => 1,
                _ => 0
            };
        }
        return cidr;
    }

    /// <summary>
    /// Calculates the network address from an IP and subnet mask.
    /// </summary>
    private static string CalculateNetworkAddress(string ipAddress, string subnetMask)
    {
        if (!IPAddress.TryParse(ipAddress, out var ip) || !IPAddress.TryParse(subnetMask, out var mask))
            return ipAddress;

        var ipBytes = ip.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        var networkBytes = new byte[ipBytes.Length];

        for (int i = 0; i < ipBytes.Length; i++)
            networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);

        return new IPAddress(networkBytes).ToString();
    }

    // ================================================================
    // Speed Test Commands
    // ================================================================

    private bool CanRunSpeedTest() => !IsTestingSpeed;
    private bool CanCancelSpeedTest() => IsTestingSpeed;

    [RelayCommand(CanExecute = nameof(CanRunSpeedTest))]
    private async Task RunSpeedTestAsync()
    {
        _speedCts = new CancellationTokenSource();
        IsTestingSpeed = true;
        HasSpeedResult = false;
        SpeedTestDownload = "—";
        SpeedTestUpload = "—";
        SpeedTestLatency = "—";
        SpeedTestJitter = "—";
        SpeedTestServer = "—";
        SpeedTestProgress = "Initialising...";
        SpeedTestPercent = 0;
        StatusText = "Running speed test...";

        var progress = new Progress<Core.Models.SpeedTestProgress>(p =>
        {
            SpeedTestProgress = p.Phase;
            SpeedTestPercent = p.PercentComplete;
        });

        try
        {
            var result = await _speedTestService.RunTestAsync(progress, _speedCts.Token);

            SpeedTestDownload = result.DownloadFormatted;
            SpeedTestUpload = result.UploadFormatted;
            SpeedTestLatency = $"{result.LatencyMs:F1} ms";
            SpeedTestJitter = $"{result.JitterMs:F1} ms";
            SpeedTestServer = result.ServerName;
            HasSpeedResult = true;

            SpeedTestProgress = $"Completed at {result.TestedAt:HH:mm:ss}";
            StatusText = $"Speed test complete — Down: {result.DownloadFormatted} | Up: {result.UploadFormatted} | Ping: {result.LatencyMs:F1}ms";
        }
        catch (OperationCanceledException)
        {
            SpeedTestProgress = "Test cancelled";
            StatusText = "Speed test cancelled";
        }
        catch (HttpRequestException ex)
        {
            SpeedTestProgress = $"Network error: {ex.Message}";
            StatusText = $"Speed test failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            SpeedTestProgress = $"Error: {ex.Message}";
            StatusText = $"Speed test error: {ex.Message}";
        }
        finally
        {
            IsTestingSpeed = false;
            _speedCts?.Dispose();
            _speedCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelSpeedTest))]
    private void CancelSpeedTest() => _speedCts?.Cancel();

    // ================================================================
    // Active Connections Commands
    // ================================================================

    [RelayCommand]
    private async Task LoadConnectionsAsync()
    {
        IsLoadingConnections = true;
        Connections.Clear();
        FilteredConnections.Clear();
        StatusText = "Loading active connections...";

        try
        {
            var connections = await _connectionMonitorService.GetActiveConnectionsAsync();

            foreach (var conn in connections)
                Connections.Add(conn);

            ApplyConnectionFilter();
            StatusText = $"{connections.Count} active connection(s) found";
        }
        catch (Exception ex)
        {
            StatusText = $"Connection error: {ex.Message}";
        }
        finally
        {
            IsLoadingConnections = false;
        }
    }

    [RelayCommand]
    private async Task RefreshConnectionsAsync() => await LoadConnectionsAsync();

    partial void OnConnectionFilterChanged(string value) => ApplyConnectionFilter();

    private void ApplyConnectionFilter()
    {
        FilteredConnections.Clear();

        var source = string.IsNullOrWhiteSpace(ConnectionFilter)
            ? Connections
            : new ObservableCollection<ConnectionEntry>(
                Connections.Where(c =>
                    c.ProcessName.Contains(ConnectionFilter, StringComparison.OrdinalIgnoreCase) ||
                    c.LocalAddress.Contains(ConnectionFilter, StringComparison.OrdinalIgnoreCase) ||
                    c.RemoteAddress.Contains(ConnectionFilter, StringComparison.OrdinalIgnoreCase) ||
                    c.State.Contains(ConnectionFilter, StringComparison.OrdinalIgnoreCase) ||
                    c.Protocol.Contains(ConnectionFilter, StringComparison.OrdinalIgnoreCase) ||
                    c.LocalPort.ToString().Contains(ConnectionFilter) ||
                    c.RemotePort.ToString().Contains(ConnectionFilter) ||
                    c.ProcessId.ToString().Contains(ConnectionFilter)));

        foreach (var conn in source)
            FilteredConnections.Add(conn);
    }

    [RelayCommand]
    private async Task KillConnectionProcessAsync()
    {
        if (SelectedConnection is null) return;

        var confirmed = DialogHelper.Confirm(
            $"Terminate process \"{SelectedConnection.ProcessName}\" (PID {SelectedConnection.ProcessId})?\n\n" +
            "This will close the process and all its network connections.",
            "COR Cleanup — Kill Process");

        if (!confirmed) return;

        try
        {
            await _connectionMonitorService.KillProcessAsync(SelectedConnection.ProcessId);
            StatusText = $"Process {SelectedConnection.ProcessName} (PID {SelectedConnection.ProcessId}) terminated";

            // Refresh the connections list
            await LoadConnectionsAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Kill failed: {ex.Message}";
        }
    }
}
