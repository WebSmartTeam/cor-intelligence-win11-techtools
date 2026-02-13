namespace CORCleanup.Core.Models;

public sealed class BatteryInfo
{
    public required long DesignCapacityMwh { get; init; }
    public required long FullChargeCapacityMwh { get; init; }
    public required int CycleCount { get; init; }
    public required string Chemistry { get; init; }
    public required string Manufacturer { get; init; }
    public required int ChargePercent { get; init; }
    public required bool IsCharging { get; init; }
    public required bool HasBattery { get; init; }

    public double HealthPercent =>
        DesignCapacityMwh > 0
            ? (double)FullChargeCapacityMwh / DesignCapacityMwh * 100
            : 0;

    public bool NeedsReplacement => HealthPercent < 80 && HasBattery;

    public string DesignCapacityFormatted =>
        $"{DesignCapacityMwh / 1000.0:F1} Wh";

    public string FullChargeFormatted =>
        $"{FullChargeCapacityMwh / 1000.0:F1} Wh";
}
