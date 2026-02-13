namespace CORCleanup.Core.Models;

/// <summary>
/// A single Wi-Fi access point discovered during a scan.
/// One SSID can have multiple BSSIDs (APs), each represented separately.
/// </summary>
public sealed class WifiNetwork
{
    public required string Ssid { get; init; }
    public required string Bssid { get; init; }
    public required int Channel { get; init; }
    public required int SignalPercent { get; init; }
    public required string RadioType { get; init; }
    public required string Authentication { get; init; }
    public required string Encryption { get; init; }
    public required string NetworkType { get; init; }

    public string Band => Channel <= 14 ? "2.4 GHz" : "5 GHz";

    public string SignalQuality => SignalPercent switch
    {
        >= 80 => "Excellent",
        >= 60 => "Good",
        >= 40 => "Fair",
        >= 20 => "Poor",
        _ => "Very Poor"
    };

    /// <summary>Approximate dBm from percentage: dBm = (percent / 2) - 100</summary>
    public int ApproximateDbm => (SignalPercent / 2) - 100;

    public string SignalDisplay => $"{SignalPercent}% ({ApproximateDbm} dBm)";

    public string WifiStandard => RadioType switch
    {
        string r when r.Contains("802.11be") => "Wi-Fi 7",
        string r when r.Contains("802.11ax") => "Wi-Fi 6",
        string r when r.Contains("802.11ac") => "Wi-Fi 5",
        string r when r.Contains("802.11n") => "Wi-Fi 4",
        _ => RadioType
    };
}

/// <summary>
/// A single signal reading from the connected network, used for monitoring over time.
/// </summary>
public sealed class WifiSignalReading
{
    public required DateTime Timestamp { get; init; }
    public required int SignalPercent { get; init; }
    public int? LinkSpeedMbps { get; init; }
    public int? Channel { get; init; }
    public string? Ssid { get; init; }

    public string TimeDisplay => Timestamp.ToString("HH:mm:ss");
    public int ApproximateDbm => (SignalPercent / 2) - 100;
}

/// <summary>
/// Aggregated channel usage info for the channel congestion chart.
/// </summary>
public sealed class ChannelUsageInfo
{
    public required int Channel { get; init; }
    public required string Band { get; init; }
    public required int NetworkCount { get; init; }
    public required int StrongestSignal { get; init; }

    public string Display => $"Ch {Channel}";

    public string CongestionLevel => NetworkCount switch
    {
        0 => "Free",
        1 => "Low",
        2 or 3 => "Moderate",
        _ => "Congested"
    };
}
