using System.Reactive.Concurrency;
using System.Threading.Tasks;
using HomeAssistantGenerated;
using NetDaemon.Extensions.Scheduler;
using NetDaemon.HassModel.Entities;

namespace HomeAutomationNetDaemon.Apps.WorkFromHome;

[NetDaemonApp]
public class WorkFromHomeLedCalendarSchedule
{
    private readonly Entities entities;
    private readonly CalendarSynchronizer calendarSynchronizer;
    private readonly ILogger<WorkFromHomeLedCalendarSchedule> logger;
    private bool currentlyBusy;

    public WorkFromHomeLedCalendarSchedule(
        Entities entities,
        IScheduler scheduler,
        CalendarSynchronizer calendarSynchronizer,
        ILogger<WorkFromHomeLedCalendarSchedule> logger
    )
    {
        this.entities = entities;
        this.calendarSynchronizer = calendarSynchronizer;
        this.logger = logger;
        
        // synchronize calendar every x minutes
        scheduler.RunEvery(TimeSpan.FromMinutes(10), DateTimeOffset.Now, 
            () => Task.Run(async () => await calendarSynchronizer.SynchronizeCalendar()));

        // initialize the working at home state and then listen for changes
        var workingAtHome = entities.InputBoolean.WorkingAtHome.IsOn();
        entities.InputBoolean.WorkingAtHome.StateChanges()
            .Subscribe(state =>
            {
                var newState = state.New?.IsOn() ?? false;
                if (workingAtHome == newState)
                {
                    return;
                }
                workingAtHome = newState;
                logger.LogDebug("Working at home state changed to: {WorkingAtHome}", workingAtHome);
                if (!workingAtHome)
                {
                    entities.Light.LedStripOfficeLight.TurnOff();
                    currentlyBusy = false;
                }
                else
                {
                    CheckBusyStatus();
                }
            });
        
        // check if the user is working at home and if the user is busy
        scheduler.RunEvery(TimeSpan.FromSeconds(30), DateTimeOffset.Now, () =>
        {
            if (!workingAtHome)
            {
                return;
            }
            
            CheckBusyStatus();
        });
    }

    private void CheckBusyStatus()
    {
        var isBusy = IsCurrentlyBusy();
        if (currentlyBusy == isBusy)
        {
            return;
        }
        logger.LogDebug("Currently busy state changed to: {CurrentlyBusy}", isBusy);
        currentlyBusy = isBusy;
        AdjustLedToBusyState(isBusy);
    }

    private bool IsCurrentlyBusy()
    {
        // get DateTime for Europe/Warsaw timezone
        var warsawTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        var utcNow = DateTime.UtcNow;
        var warsawTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, warsawTimeZone);
        var time = TimeOnly.FromDateTime(warsawTime);
        return calendarSynchronizer.IsBusy(time);
    }
    
    private void AdjustLedToBusyState(bool isBusy)
    {
        if (isBusy)
        {
            entities.Light.LedStripOfficeLight.TurnOn(brightnessPct:80, rgbColor: [255, 0, 0]);
        }
        else
        {
            entities.Light.LedStripOfficeLight.TurnOn(brightnessPct: 10, rgbColor: [0, 0, 255]);
        }
    }
}