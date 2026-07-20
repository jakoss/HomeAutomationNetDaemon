using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Microsoft.Extensions.Configuration;

namespace HomeAutomationNetDaemon.Apps.WorkFromHome;

public class CalendarSynchronizer(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<CalendarSynchronizer> logger,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeZoneInfo WarsawTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private volatile List<BusyTime> _currentBusyTimeSlots = [];
    private volatile Dictionary<string, List<BusyTime>> _busyTimeSlotsByCalendar = new(StringComparer.Ordinal);
    
    public async Task SynchronizeCalendar(CancellationToken cancellationToken = default)
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
                synchronizedSlotsByCalendar[calendarUrl] = await GetBusyTimeSlotsAsync(client, calendarUrl, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
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

    public BusyStatus GetBusyStatus() => GetBusyStatus(_timeProvider.GetUtcNow());

    public BusyStatus GetBusyStatus(TimeOnly time)
    {
        var warsawToday = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), WarsawTimeZone).Date;
        var localDateTime = DateTime.SpecifyKind(warsawToday.Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
        return GetBusyStatus(localDateTime.ToUtc(WarsawTimeZone));
    }

    private BusyStatus GetBusyStatus(DateTimeOffset time)
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

    private async Task<List<BusyTime>> GetBusyTimeSlotsAsync(
        HttpClient client,
        string calendarUrl,
        CancellationToken cancellationToken)
    {
        var calendarString = await client.GetStringAsync(calendarUrl, cancellationToken);

        logger.LogDebug("Parsing calendar...");
        // Outlook can export this non-standard timezone identifier.
        calendarString = calendarString.Replace(
            "TZID=Customized Time Zone:",
            "TZID=Central European Standard Time:");

        var calendar = Calendar.Load(calendarString)
            ?? throw new InvalidOperationException("Failed to parse calendar");

        var warsawToday = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), WarsawTimeZone).Date;
        var dayStart = warsawToday.ToUtc(WarsawTimeZone);
        var dayEnd = warsawToday.AddDays(1).ToUtc(WarsawTimeZone);
        var longestEventDuration = calendar.Events
            .Select(calendarEvent => calendarEvent.EffectiveDuration.ToTimeSpanUnspecified())
            .DefaultIfEmpty(TimeSpan.Zero)
            .Max();
        var occurrenceSearchStart = new CalDateTime((dayStart - longestEventDuration).UtcDateTime);
        var occurrenceSearchEnd = new CalDateTime(dayEnd.UtcDateTime);

        return calendar.GetOccurrences<CalendarEvent>(occurrenceSearchStart)
            .TakeWhileBefore(occurrenceSearchEnd)
            .Where(occurrence => occurrence.Source is CalendarEvent calendarEvent && IsBlocking(calendarEvent))
            .Select(ConvertToBusyTime)
            .Where(slot => slot.Start < dayEnd && slot.End > dayStart)
            .OrderBy(slot => slot.Start)
            .ToList();
    }

    private static bool IsBlocking(CalendarEvent calendarEvent)
    {
        var outlookBusyStatus = calendarEvent.Properties["X-MICROSOFT-CDO-BUSYSTATUS"]?.Value?.ToString();
        return calendarEvent.IsActive
            && !string.Equals(calendarEvent.Transparency, "TRANSPARENT", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(outlookBusyStatus, "FREE", StringComparison.OrdinalIgnoreCase);
    }

    private static BusyTime ConvertToBusyTime(Occurrence occurrence)
    {
        var calendarEvent = (CalendarEvent) occurrence.Source;
        var status = calendarEvent.Properties["X-MICROSOFT-CDO-BUSYSTATUS"]?.Value;
        var busyStatus = status switch
        {
            "TENTATIVE" => BusyStatus.BusyTentative,
            var _ => BusyStatus.Busy
        };
        var endTime = occurrence.Period.EffectiveEndTime ?? occurrence.Period.StartTime;
        return new BusyTime(
            occurrence.Period.StartTime.ToUtc(WarsawTimeZone),
            endTime.ToUtc(WarsawTimeZone),
            busyStatus
        );
    }

    private record BusyTime(DateTimeOffset Start, DateTimeOffset End, BusyStatus BusyStatus);
}
