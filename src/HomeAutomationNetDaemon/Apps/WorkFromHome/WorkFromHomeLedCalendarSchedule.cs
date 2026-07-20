using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using HomeAssistantGenerated;
using NetDaemon.Extensions.Scheduler;
using NetDaemon.HassModel.Entities;

namespace HomeAutomationNetDaemon.Apps.WorkFromHome;

[NetDaemonApp]
public class WorkFromHomeLedCalendarSchedule : IDisposable
{
    private readonly Entities entities;
    private readonly CalendarSynchronizer calendarSynchronizer;
    private readonly ILogger<WorkFromHomeLedCalendarSchedule> logger;
    private readonly CompositeDisposable subscriptions = [];
    private BusyStatus currentBusyStatus;

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

        // Synchronize serially so a slow refresh cannot overlap the next one.
        subscriptions.Add(
            Observable.Timer(scheduler.Now, TimeSpan.FromMinutes(10), scheduler)
                .Select(_ => Observable.FromAsync(calendarSynchronizer.SynchronizeCalendar)
                    .Catch<Unit, Exception>(e =>
                    {
                        logger.LogError(e, "Calendar synchronization failed");
                        return Observable.Empty<Unit>();
                    }))
                .Concat()
                .Subscribe());

        // initialize the working at home state and then listen for changes
        var workingAtHome = entities.InputBoolean.WorkingAtHome.IsOn();
        subscriptions.Add(entities.InputBoolean.WorkingAtHome.StateChanges()
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
                    entities.Light.OfficeDeskLamp.TurnOff();
                    currentBusyStatus = BusyStatus.Free;
                }
                else
                {
                    CheckBusyStatus();
                }
            }));
        
        // check if the user is working at home and if the user is busy
        subscriptions.Add(
            scheduler.RunEvery(TimeSpan.FromSeconds(30), scheduler.Now, () =>
            {
                if (!workingAtHome)
                {
                    return;
                }

                CheckBusyStatus();
            }));
    }

    private void CheckBusyStatus()
    {
        var newBusyState = GetBusyStatus();
        if (currentBusyStatus != newBusyState)
        {
            logger.LogDebug("Currently busy state changed to: {CurrentlyBusy}", newBusyState);
            currentBusyStatus = newBusyState;
        }
        
        logger.LogDebug("Current busy state: {CurrentlyBusy}", newBusyState);
        
        AdjustLedToBusyState(newBusyState);
    }

    private BusyStatus GetBusyStatus()
    {
        return calendarSynchronizer.GetBusyStatus();
    }
    
    private void AdjustLedToBusyState(BusyStatus busyStatus)
    {
        if (busyStatus == BusyStatus.Busy)
        {
            logger.LogDebug("Adjusting led to busy state");
            entities.Light.LedStripOfficeLight.TurnOn(brightnessPct:80, rgbColor: [255, 0, 0]);
            entities.Light.OfficeDeskLamp.TurnOn(brightnessPct: 80, rgbColor: [255, 0, 0]);
        }
        else if (busyStatus == BusyStatus.BusyTentative)
        {
            logger.LogDebug("Adjusting led to busy tentative state");
            entities.Light.LedStripOfficeLight.TurnOn(brightnessPct:80, rgbColor: [255, 4, 164]);
            entities.Light.OfficeDeskLamp.TurnOn(brightnessPct: 80, rgbColor: [255, 161, 10]);
        }
        else
        {
            logger.LogDebug("Adjusting led to free state");
            entities.Light.LedStripOfficeLight.TurnOn(brightnessPct: 10, rgbColor: [0, 0, 255]);
            entities.Light.OfficeDeskLamp.TurnOn(brightnessPct: 10, rgbColor: [0, 255, 0]);
        }
    }

    public void Dispose() => subscriptions.Dispose();
}
