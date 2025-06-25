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

        try
        {
            var client = httpClientFactory.CreateClient();
            var calendarString = await client.GetStringAsync(
                "https://outlook.office365.com/owa/calendar/e6a4ba5bda1f4aa19ec77875500106c1@euvic.pl/beca9010732e48cb8ffd2d4660a9f76e2077511452080529152/calendar.ics");

            logger.LogDebug("Parsing calendar...");
            // first, replace all occurances of Customizes Timezone with the CET
            calendarString =
                calendarString.Replace("TZID=Customized Time Zone:", "TZID=Central European Standard Time:");

            var calendar = Calendar.Load(calendarString);

            if (calendar is null)
            {
                throw new Exception("Failed to parse calendar");
            }

            var today = new CalDateTime(DateTime.Today, "Europe/Warsaw");
            var busyTimeSlotsForOneTimeEvents = calendar.Events
                .Where(e => e.DtStart?.Date == today.Date)
                .Select(ConvertToBusyTime);
            
            var busyTimeSlotsForRecurrentEvents = calendar.GetOccurrences<CalendarEvent>(today)
                .Where(e => e.Source is CalendarEvent && e.Period.StartTime.Date == today.Date)
                .Select(e => e.Source)
                .Cast<CalendarEvent>()
                .Select(ConvertToBusyTime);
            
            currentBusyTimeSlots = busyTimeSlotsForOneTimeEvents.Concat(busyTimeSlotsForRecurrentEvents).OrderBy(e => e.Start).ToList();

            logger.LogDebug("Calendar synchronized");
        } catch (Exception e)
        {
            logger.LogError(e, "Failed to synchronize calendar");
        }
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

    private static TimeOnly ConvertToLocalTime(CalDateTime calDateTime) =>
        calDateTime switch
        {
            { IsUtc: true } => calDateTime.AsUtc.ToLocalDateTime().TimeOfDay.ToTimeOnly(),
            var _ => TimeOnly.FromDateTime(calDateTime.ToTimeZone("Europe/Warsaw").Value)
        };

    private record BusyTime(TimeOnly Start, TimeOnly End, BusyStatus BusyStatus);
}