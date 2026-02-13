using System.ServiceProcess;

namespace CORCleanup.Core.Models;

public enum ServiceCategory
{
    MicrosoftCore,
    MicrosoftOptional,
    ThirdParty
}

public sealed class ServiceEntry
{
    public required string ServiceName { get; init; }
    public required string DisplayName { get; init; }
    public required ServiceControllerStatus Status { get; init; }
    public required ServiceStartMode StartType { get; init; }
    public string? Description { get; init; }
    public string? PathToExecutable { get; init; }
    public ServiceCategory Category { get; init; } = ServiceCategory.ThirdParty;
    public bool IsSafeToDisable { get; init; }
    public string? SafeToDisableReason { get; init; }

    public string StatusText => Status switch
    {
        ServiceControllerStatus.Running => "Running",
        ServiceControllerStatus.Stopped => "Stopped",
        ServiceControllerStatus.Paused => "Paused",
        ServiceControllerStatus.StartPending => "Starting",
        ServiceControllerStatus.StopPending => "Stopping",
        _ => Status.ToString()
    };
}
