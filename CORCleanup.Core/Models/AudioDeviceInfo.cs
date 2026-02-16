namespace CORCleanup.Core.Models;

/// <summary>
/// Audio device from Win32_SoundDevice WMI class.
/// </summary>
public sealed class AudioDeviceInfo
{
    public required string Name { get; init; }
    public required string Manufacturer { get; init; }
    public required string Status { get; init; }
    public required string DeviceId { get; init; }
}
