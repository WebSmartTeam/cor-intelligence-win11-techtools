using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface IEventLogService
{
    Task<List<EventLogEntry>> GetRecentEventsAsync(
        int days = 7,
        EventSeverity minimumSeverity = EventSeverity.Error);
}
