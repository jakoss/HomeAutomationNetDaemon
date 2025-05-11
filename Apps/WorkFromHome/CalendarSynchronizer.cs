using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
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

        var calendar = Ical.Net.Calendar.Load(calendarString);
        var busyTimeSlots = calendar.GetOccurrences<CalendarEvent>(CalDateTime.Today, CalDateTime.Today.AddDays(1))
            .Where(e => e.Source is CalendarEvent)
            .Select(e => e.Source)
            .Cast<CalendarEvent>()
            .Select(ConvertToBusyTime)
            .ToList();

        // get rid of the slots that overlap. We are only interested in slots that are busy or not
        // so if we have a slot 1 AM - 3 AM and 2 AM - 4 AM, we want to have one slot 1 AM - 4 AM as a result
        for (var i = 0; i < busyTimeSlots.Count; i++)
        {
            for (var j = i + 1; j < busyTimeSlots.Count; j++)
            {
                if (busyTimeSlots[i].End < busyTimeSlots[j].Start ||
                    busyTimeSlots[i].Start > busyTimeSlots[j].End) continue;
                busyTimeSlots[i] = new BusyTime(busyTimeSlots[i].Start, busyTimeSlots[j].End);
                busyTimeSlots.RemoveAt(j);
                j--;
            }
        }

        currentBusyTimeSlots = busyTimeSlots;
        logger.LogDebug("Calendar synchronized");
    }
    
    public bool IsBusy(TimeOnly time) => currentBusyTimeSlots.Any(busyTime => busyTime.Start <= time && busyTime.End >= time);
    
    private static BusyTime ConvertToBusyTime(CalendarEvent calendarEvent)
    {
        return new BusyTime(
            ConvertToLocalTime(calendarEvent.DtStart!),
            ConvertToLocalTime(calendarEvent.DtEnd!)
        );
    }

    private static TimeOnly ConvertToLocalTime(CalDateTime calDateTime) =>
        calDateTime switch
        {
            { IsUtc: true } => calDateTime.AsUtc.ToLocalDateTime().TimeOfDay.ToTimeOnly(),
            _ => TimeOnly.FromDateTime(calDateTime.ToTimeZone("Europe/Warsaw").Value)
        };

    private record BusyTime(TimeOnly Start, TimeOnly End);
}