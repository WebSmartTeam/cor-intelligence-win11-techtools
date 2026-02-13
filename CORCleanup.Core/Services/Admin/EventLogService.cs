using System.Diagnostics;
using System.Runtime.Versioning;
using CORCleanup.Core.Interfaces;
using CORCleanup.Core.Models;
using EventLogEntryModel = CORCleanup.Core.Models.EventLogEntry;

namespace CORCleanup.Core.Services.Admin;

/// <summary>
/// Reads Critical and Error events from System and Application event logs.
/// System/Application logs are readable without elevation on Win11.
/// Security log requires admin (we skip it for safety).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EventLogService : IEventLogService
{
    private static readonly string[] LogNames = { "System", "Application" };

    // Common Event IDs mapped to human-readable explanations
    private static readonly Dictionary<(string Source, long EventId), string> KnownEvents = new()
    {
        [("Kernel-Power", 41)] = "Unexpected shutdown — system lost power or crashed (BSOD, power failure, or forced shutdown)",
        [("Kernel-Power", 109)] = "Kernel processor power state changed — possible power issue",
        [("WHEA-Logger", 18)] = "Hardware error detected — check CPU, RAM, or disk health",
        [("WHEA-Logger", 19)] = "Corrected hardware error — component degrading",
        [("EventLog", 6008)] = "Previous shutdown was unexpected — system did not shut down cleanly",
        [("Service Control Manager", 7031)] = "Service terminated unexpectedly — may need reinstalling",
        [("Service Control Manager", 7034)] = "Service terminated unexpectedly — check service health",
        [("DCOM", 10016)] = "DCOM permission error — usually benign but indicates misconfigured COM permissions",
        [("DistributedCOM", 10016)] = "DCOM permission error — usually benign",
        [("Disk", 11)] = "Disk controller error — possible failing disk or cable issue",
        [("Disk", 7)] = "Bad block detected on disk — run chkdsk and check SMART data",
        [("ntfs", 55)] = "NTFS file system corruption detected — run chkdsk",
        [("Application Error", 1000)] = "Application crashed — check faulting module for driver or software issue",
        [("Windows Error Reporting", 1001)] = "Application fault bucket — crash report submitted",
        [("BugCheck", 1001)] = "Blue screen occurred — check minidump for faulting driver",
    };

    public Task<List<EventLogEntryModel>> GetRecentEventsAsync(
        int days = 7,
        EventSeverity minimumSeverity = EventSeverity.Error)
    {
        return Task.Run(() =>
        {
            var results = new List<EventLogEntryModel>();
            var cutoff = DateTime.Now.AddDays(-days);

            foreach (var logName in LogNames)
            {
                try
                {
                    using var log = new EventLog(logName);

                    // EventLog.Entries is not thread-safe, iterate carefully
                    var entries = log.Entries;
                    for (var i = entries.Count - 1; i >= 0; i--)
                    {
                        try
                        {
                            var entry = entries[i];

                            if (entry.TimeGenerated < cutoff)
                                break; // Entries are chronological — stop when past cutoff

                            var severity = MapSeverity(entry.EntryType);
                            if (severity > minimumSeverity)
                                continue; // Skip less severe events

                            var explanation = KnownEvents.TryGetValue(
                                (entry.Source, entry.InstanceId), out var known)
                                ? known
                                : null;

                            results.Add(new EventLogEntryModel
                            {
                                TimeGenerated = entry.TimeGenerated,
                                Severity = severity,
                                Source = entry.Source,
                                EventId = entry.InstanceId,
                                LogName = logName,
                                Message = TruncateMessage(entry.Message, 500),
                                HumanReadableExplanation = explanation
                            });
                        }
                        catch
                        {
                            // Individual entry read failure — skip
                        }
                    }
                }
                catch
                {
                    // Log not accessible — skip
                }
            }

            return results
                .OrderByDescending(e => e.TimeGenerated)
                .ToList();
        });
    }

    private static EventSeverity MapSeverity(EventLogEntryType entryType) => entryType switch
    {
        EventLogEntryType.Error => EventSeverity.Error,
        EventLogEntryType.Warning => EventSeverity.Warning,
        EventLogEntryType.Information => EventSeverity.Information,
        _ => EventSeverity.Information
    };

    private static string TruncateMessage(string message, int maxLength) =>
        message.Length <= maxLength ? message : message[..maxLength] + "...";
}
