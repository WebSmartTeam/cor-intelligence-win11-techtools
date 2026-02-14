using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IProcessExplorerService
{
    /// <summary>
    /// Gets a snapshot of all running processes with CPU and memory usage.
    /// CPU usage requires a brief sampling interval (~500ms).
    /// </summary>
    Task<List<ProcessEntry>> GetProcessesAsync(CancellationToken ct = default);

    /// <summary>
    /// Kills a process by PID. Returns true on success.
    /// </summary>
    Task<bool> KillProcessAsync(int pid);

    /// <summary>
    /// Opens the file location of a process in Explorer.
    /// </summary>
    void OpenFileLocation(string? filePath);
}
