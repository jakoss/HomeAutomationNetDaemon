using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using NodaTime.Extensions;

namespace HomeAutomationNetDaemon.Apps.WorkFromHome;

public class CalendarSynchronizer(IHttpClientFactory httpClientFactory, ILogger<CalendarSynchronizer> logger)
{
    private volatile List<BusyTime> currentBusyTimeSlots = [];
    
    public async Task SynchronizeCalendar()
    {
        logger.LogDebug("Synchronizing calendar...");
        var client = httpClientFactory.CreateClient();
        var calendarString = await client.GetStringAsync("https://outlook.office365.com/owa/calendar/e6a4ba5bda1f4aa19ec77875500106c1@euvic.pl/acb154cdbd98441480d6baa2be4e685b16664996553945727195/calendar.ics");
    
        logger.LogDebug("Parsing calendar...");
        // first, replace all occurances of Customizes Timezone with the CET
        calendarString = calendarString.Replace("TZID=Customized Time Zone:", "TZID=Central European Standard Time:");

        var calendar = Calendar.Load(calendarString);

        if (calendar is null)
        {
            throw new Exception("Failed to parse calendar");
        }
        var busyTimeSlots = calendar.GetOccurrences<CalendarEvent>(CalDateTime.Today)
            .Where(e => e.Source is CalendarEvent)
            .Select(e => e.Source)
            .Cast<CalendarEvent>()
            .Select(ConvertToBusyTime)
            .ToList();
        
        currentBusyTimeSlots = busyTimeSlots;
        
        logger.LogDebug("Calendar synchronized");
    }

    public BusyStatus GetBusyStatus(TimeOnly time)
    {
        var events = currentBusyTimeSlots.Where(busyTime => busyTime.Start <= time && busyTime.End >= time).ToArray();

        return events switch
        {
            { Length: 0 } => BusyStatus.Free,
            { Length: 1 } => events[0].BusyStatus,
            { Length: > 1 } when events.Any(e => e.BusyStatus == BusyStatus.Busy) => BusyStatus.Busy,
            var _ => BusyStatus.BusyTentative
        };
    }
    
    private static BusyTime ConvertToBusyTime(CalendarEvent calendarEvent)
    {
        var busyStatus = calendarEvent.Summary switch
        {
            "Wstępna akceptacja" => BusyStatus.BusyTentative,
            var _ => BusyStatus.Busy
        };
        return new BusyTime(
            ConvertToLocalTime(calendarEvent.DtStart!),
            ConvertToLocalTime(calendarEvent.DtEnd!),
            busyStatus
        );
    }

    private static TimeOnly ConvertToLocalTime(CalDateTime calDateTime) =>
        calDateTime switch
        {
            { IsUtc: true } => calDateTime.AsUtc.ToLocalDateTime().TimeOfDay.ToTimeOnly(),
            var _ => TimeOnly.FromDateTime(calDateTime.ToTimeZone("Europe/Warsaw").Value)
        };

    private record BusyTime(TimeOnly Start, TimeOnly End, BusyStatus BusyStatus);
}