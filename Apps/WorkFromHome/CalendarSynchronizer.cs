using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Microsoft.Extensions.Configuration;

namespace HomeAutomationNetDaemon.Apps.WorkFromHome;

public class CalendarSynchronizer(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<CalendarSynchronizer> logger)
{
    private volatile List<BusyTime> _currentBusyTimeSlots = [];
    private volatile Dictionary<string, List<BusyTime>> _busyTimeSlotsByCalendar = new(StringComparer.Ordinal);
    
    public async Task SynchronizeCalendar()
    {
        var calendarUrls = configuration.GetSection("WorkFromHome:CalendarUrls")
            .GetChildren()
            .Select(section => section.Value)
            .OfType<string>()
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (calendarUrls.Length == 0)
        {
            logger.LogError("No calendar URLs are configured in WorkFromHome:CalendarUrls");
            return;
        }

        logger.LogDebug("Synchronizing {CalendarCount} calendars...", calendarUrls.Length);

        var existingSlotsByCalendar = _busyTimeSlotsByCalendar;
        var synchronizedSlotsByCalendar = new Dictionary<string, List<BusyTime>>(StringComparer.Ordinal);
        var client = httpClientFactory.CreateClient();

        foreach (var calendarUrl in calendarUrls)
        {
            try
            {
                synchronizedSlotsByCalendar[calendarUrl] = await GetBusyTimeSlotsAsync(client, calendarUrl);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to synchronize one calendar");

                // Retain the last known slots for this calendar so a temporary failure
                // cannot incorrectly turn the combined schedule to free.
                if (existingSlotsByCalendar.TryGetValue(calendarUrl, out var existingSlots))
                {
                    synchronizedSlotsByCalendar[calendarUrl] = existingSlots;
                }
            }
        }

        _busyTimeSlotsByCalendar = synchronizedSlotsByCalendar;
        _currentBusyTimeSlots = synchronizedSlotsByCalendar.Values
            .SelectMany(slots => slots)
            .OrderBy(slot => slot.Start)
            .ToList();

        logger.LogDebug("{CalendarCount} calendars synchronized into {SlotCount} busy slots", calendarUrls.Length, _currentBusyTimeSlots.Count);
    }

    public BusyStatus GetBusyStatus(TimeOnly time)
    {
        var events = _currentBusyTimeSlots.Where(busyTime => busyTime.Start <= time && busyTime.End > time).ToArray();

        return events switch
        {
            { Length: 0 } => BusyStatus.Free,
            { Length: 1 } => events[0].BusyStatus,
            { Length: > 1 } when events.Any(e => e.BusyStatus == BusyStatus.Busy) => BusyStatus.Busy,
            var _ => BusyStatus.BusyTentative
        };
    }

    private async Task<List<BusyTime>> GetBusyTimeSlotsAsync(HttpClient client, string calendarUrl)
    {
        var calendarString = await client.GetStringAsync(calendarUrl);

        logger.LogDebug("Parsing calendar...");
        // Outlook can export this non-standard timezone identifier.
        calendarString = calendarString.Replace(
            "TZID=Customized Time Zone:",
            "TZID=Central European Standard Time:");

        var calendar = Calendar.Load(calendarString)
            ?? throw new InvalidOperationException("Failed to parse calendar");

        var today = new CalDateTime(DateTime.Today, "Europe/Warsaw");
        var busyTimeSlotsForOneTimeEvents = calendar.Events
            .Where(e => e.DtStart?.Date == today.Date)
            .Select(ConvertToBusyTime);

        var busyTimeSlotsForRecurrentEvents = calendar.GetOccurrences<CalendarEvent>(today)
            .Where(e => e.Source is CalendarEvent && e.Period.StartTime.Date == today.Date)
            .Select(e => e.Source)
            .Cast<CalendarEvent>()
            .Select(ConvertToBusyTime);

        return busyTimeSlotsForOneTimeEvents
            .Concat(busyTimeSlotsForRecurrentEvents)
            .OrderBy(slot => slot.Start)
            .ToList();
    }
    
    private static BusyTime ConvertToBusyTime(CalendarEvent calendarEvent)
    {
        var status = calendarEvent.Properties["X-MICROSOFT-CDO-BUSYSTATUS"]?.Value;
        var busyStatus = status switch
        {
            "TENTATIVE" => BusyStatus.BusyTentative,
            var _ => BusyStatus.Busy
        };
        return new BusyTime(
            ConvertToLocalTime(calendarEvent.DtStart!),
            ConvertToLocalTime(calendarEvent.DtEnd!),
            busyStatus
        );
    }

    internal static TimeOnly ConvertToLocalTime(CalDateTime calDateTime) =>
        TimeOnly.FromDateTime(calDateTime.ToTimeZone("Europe/Warsaw").Value);

    private record BusyTime(TimeOnly Start, TimeOnly End, BusyStatus BusyStatus);
}
