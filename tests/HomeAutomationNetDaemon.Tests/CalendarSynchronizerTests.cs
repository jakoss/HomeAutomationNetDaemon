using System.Net;
using HomeAutomationNetDaemon.Apps.WorkFromHome;
using Ical.Net.DataTypes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeAutomationNetDaemon.Tests;

public class CalendarSynchronizerTests
{
    private const string CalendarOne = "https://calendar.test/one.ics";
    private const string CalendarTwo = "https://calendar.test/two.ics";
    private static readonly TimeZoneInfo WarsawTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    [Test]
    public async Task UtcTimeInSummerIsConvertedToWarsawDaylightTime()
    {
        var utc = new CalDateTime(new DateTime(2026, 7, 15, 13, 30, 0, DateTimeKind.Utc));

        var result = CalendarSynchronizer.ConvertToLocalTime(utc);

        await Assert.That(result).IsEqualTo(new TimeOnly(15, 30));
    }

    [Test]
    public async Task UtcTimeInWinterIsConvertedToWarsawStandardTime()
    {
        var utc = new CalDateTime(new DateTime(2026, 1, 15, 14, 30, 0, DateTimeKind.Utc));

        var result = CalendarSynchronizer.ConvertToLocalTime(utc);

        await Assert.That(result).IsEqualTo(new TimeOnly(15, 30));
    }

    [Test]
    public async Task WarsawTimeRemainsUnchanged()
    {
        var warsaw = new CalDateTime(new DateTime(2026, 7, 15, 15, 30, 0), "Europe/Warsaw");

        var result = CalendarSynchronizer.ConvertToLocalTime(warsaw);

        await Assert.That(result).IsEqualTo(new TimeOnly(15, 30));
    }

    [Test]
    public async Task OutlookWindowsTimeZoneIsConvertedToWarsaw()
    {
        var outlookTime = new CalDateTime(
            new DateTime(2026, 7, 15, 15, 30, 0),
            "Central European Standard Time");

        var result = CalendarSynchronizer.ConvertToLocalTime(outlookTime);

        await Assert.That(result).IsEqualTo(new TimeOnly(15, 30));
    }

    [Test]
    public async Task TimeFromAnotherZoneIsConvertedToWarsaw()
    {
        var indiaTime = new CalDateTime(new DateTime(2026, 7, 15, 19, 0, 0), "India Standard Time");

        var result = CalendarSynchronizer.ConvertToLocalTime(indiaTime);

        await Assert.That(result).IsEqualTo(new TimeOnly(15, 30));
    }

    [Test]
    public async Task OneTimeUtcEventIsBusyAtItsWarsawTime()
    {
        var today = DateTime.Today;
        var localStart = DateTime.SpecifyKind(today.AddHours(15).AddMinutes(30), DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, WarsawTimeZone);
        var synchronizer = CreateSynchronizer(
            [CalendarOne],
            new StubCalendarServer().Respond(CalendarOne, Calendar(Event(startUtc, startUtc.AddMinutes(30)))));

        await synchronizer.SynchronizeCalendar();

        await Assert.That(synchronizer.GetBusyStatus(new TimeOnly(15, 45))).IsEqualTo(BusyStatus.Busy);
        await Assert.That(synchronizer.GetBusyStatus(new TimeOnly(15, 29))).IsEqualTo(BusyStatus.Free);
    }

    [Test]
    public async Task EventBoundariesUseInclusiveStartAndExclusiveEnd()
    {
        var today = DateTime.Today;
        var synchronizer = CreateSynchronizer(
            [CalendarOne],
            new StubCalendarServer().Respond(CalendarOne, Calendar(LocalEvent(today, 15, 30, 16, 0))));

        await synchronizer.SynchronizeCalendar();

        await Assert.That(synchronizer.GetBusyStatus(new TimeOnly(15, 30))).IsEqualTo(BusyStatus.Busy);
        await Assert.That(synchronizer.GetBusyStatus(new TimeOnly(16, 0))).IsEqualTo(BusyStatus.Free);
    }

    [Test]
    public async Task OneTimeEventOnAnotherDayIsIgnored()
    {
        var tomorrow = DateTime.Today.AddDays(1);
        var synchronizer = CreateSynchronizer(
            [CalendarOne],
            new StubCalendarServer().Respond(CalendarOne, Calendar(LocalEvent(tomorrow, 15, 30, 16, 0))));

        await synchronizer.SynchronizeCalendar();

        await Assert.That(synchronizer.GetBusyStatus(new TimeOnly(15, 45))).IsEqualTo(BusyStatus.Free);
    }

    [Test]
    public async Task RecurringEventOccurrenceForTodayIsIncluded()
    {
        var firstOccurrence = DateTime.Today.AddDays(-7);
        var recurringEvent = LocalEvent(firstOccurrence, 10, 0, 10, 30, "RRULE:FREQ=DAILY;COUNT=30");
        var synchronizer = CreateSynchronizer(
            [CalendarOne],
            new StubCalendarServer().Respond(CalendarOne, Calendar(recurringEvent)));

        await synchronizer.SynchronizeCalendar();

        await Assert.That(synchronizer.GetBusyStatus(new TimeOnly(10, 15))).IsEqualTo(BusyStatus.Busy);
    }

    [Test]
    public async Task EventWithoutBusyStatusDefaultsToBusy()
    {
        var today = DateTime.Today;
        var synchronizer = CreateSynchronizer(
            [CalendarOne],
            new StubCalendarServer().Respond(CalendarOne, Calendar(LocalEvent(today, 9, 0, 9, 30))));

        await synchronizer.SynchronizeCalendar();

        await Assert.That(synchronizer.GetBusyStatus(new TimeOnly(9, 15))).IsEqualTo(BusyStatus.Busy);
    }

    [Test]
    public async Task TentativeEventProducesTentativeStatus()
    {
        var today = DateTime.Today;
        var synchronizer = CreateSynchronizer(
            [CalendarOne],
            new StubCalendarServer().Respond(
                CalendarOne,
                Calendar(LocalEvent(today, 9, 0, 9, 30, "X-MICROSOFT-CDO-BUSYSTATUS:TENTATIVE"))));

        await synchronizer.SynchronizeCalendar();

        await Assert.That(synchronizer.GetBusyStatus(new TimeOnly(9, 15))).IsEqualTo(BusyStatus.BusyTentative);
    }

    [Test]
    public async Task BusyEventTakesPrecedenceOverOverlappingTentativeEvent()
    {
        var today = DateTime.Today;
        var calendar = Calendar(
            LocalEvent(today, 9, 0, 10, 0, "X-MICROSOFT-CDO-BUSYSTATUS:TENTATIVE", "tentative"),
            LocalEvent(today, 9, 30, 10, 30, uid: "busy"));
        var synchronizer = CreateSynchronizer(
            [CalendarOne],
            new StubCalendarServer().Respond(CalendarOne, calendar));

        await synchronizer.SynchronizeCalendar();

        await Assert.That(synchronizer.GetBusyStatus(new TimeOnly(9, 15))).IsEqualTo(BusyStatus.BusyTentative);
        await Assert.That(synchronizer.GetBusyStatus(new TimeOnly(9, 45))).IsEqualTo(BusyStatus.Busy);
    }

    [Test]
    public async Task EventsFromMultipleCalendarsAreCombined()
    {
        var today = DateTime.Today;
        var server = new StubCalendarServer()
            .Respond(CalendarOne, Calendar(LocalEvent(today, 9, 0, 9, 30)))
            .Respond(CalendarTwo, Calendar(LocalEvent(today, 14, 0, 14, 30)));
        var synchronizer = CreateSynchronizer([CalendarOne, CalendarTwo], server);

        await synchronizer.SynchronizeCalendar();

        await Assert.That(synchronizer.GetBusyStatus(new TimeOnly(9, 15))).IsEqualTo(BusyStatus.Busy);
        await Assert.That(synchronizer.GetBusyStatus(new TimeOnly(14, 15))).IsEqualTo(BusyStatus.Busy);
    }

    [Test]
    public async Task DuplicateAndBlankCalendarUrlsAreIgnored()
    {
        var server = new StubCalendarServer().Respond(CalendarOne, Calendar());
        var synchronizer = CreateSynchronizer(["", "  ", CalendarOne, CalendarOne], server);

        await synchronizer.SynchronizeCalendar();

        await Assert.That(server.RequestCount(CalendarOne)).IsEqualTo(1);
    }

    [Test]
    public async Task FailedRefreshRetainsLastKnownEventsForThatCalendar()
    {
        var today = DateTime.Today;
        var server = new StubCalendarServer()
            .Respond(CalendarOne, Calendar(LocalEvent(today, 11, 0, 11, 30)))
            .Fail(CalendarOne);
        var synchronizer = CreateSynchronizer([CalendarOne], server);

        await synchronizer.SynchronizeCalendar();
        await synchronizer.SynchronizeCalendar();

        await Assert.That(synchronizer.GetBusyStatus(new TimeOnly(11, 15))).IsEqualTo(BusyStatus.Busy);
    }

    [Test]
    public async Task FailureOfOneCalendarDoesNotDiscardSuccessfulCalendar()
    {
        var today = DateTime.Today;
        var server = new StubCalendarServer()
            .Fail(CalendarOne)
            .Respond(CalendarTwo, Calendar(LocalEvent(today, 14, 0, 14, 30)));
        var synchronizer = CreateSynchronizer([CalendarOne, CalendarTwo], server);

        await synchronizer.SynchronizeCalendar();

        await Assert.That(synchronizer.GetBusyStatus(new TimeOnly(14, 15))).IsEqualTo(BusyStatus.Busy);
    }

    [Test]
    public async Task InvalidCalendarDoesNotMakeSynchronizeThrow()
    {
        var synchronizer = CreateSynchronizer(
            [CalendarOne],
            new StubCalendarServer().Respond(CalendarOne, "not an icalendar"));

        await synchronizer.SynchronizeCalendar();

        await Assert.That(synchronizer.GetBusyStatus(new TimeOnly(12, 0))).IsEqualTo(BusyStatus.Free);
    }

    [Test]
    public async Task NoConfiguredCalendarsLeavesStatusFreeWithoutHttpRequest()
    {
        var server = new StubCalendarServer();
        var synchronizer = CreateSynchronizer([], server);

        await synchronizer.SynchronizeCalendar();

        await Assert.That(server.TotalRequestCount).IsEqualTo(0);
        await Assert.That(synchronizer.GetBusyStatus(new TimeOnly(12, 0))).IsEqualTo(BusyStatus.Free);
    }

    [Test]
    public async Task OutlookCustomizedTimeZoneIdentifierIsNormalized()
    {
        var today = DateTime.Today;
        var date = today.ToString("yyyyMMdd");
        var customEvent = $$"""
            BEGIN:VEVENT
            UID:custom-zone
            DTSTAMP:20260101T000000Z
            DTSTART;TZID=Customized Time Zone:{{date}}T153000
            DTEND;TZID=Customized Time Zone:{{date}}T160000
            END:VEVENT
            """;
        var synchronizer = CreateSynchronizer(
            [CalendarOne],
            new StubCalendarServer().Respond(CalendarOne, Calendar(customEvent)));

        await synchronizer.SynchronizeCalendar();

        await Assert.That(synchronizer.GetBusyStatus(new TimeOnly(15, 45))).IsEqualTo(BusyStatus.Busy);
    }

    private static CalendarSynchronizer CreateSynchronizer(
        IReadOnlyList<string> urls,
        StubCalendarServer server)
    {
        var values = urls
            .Select((url, index) => new KeyValuePair<string, string?>($"WorkFromHome:CalendarUrls:{index}", url));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new CalendarSynchronizer(
            server,
            configuration,
            NullLogger<CalendarSynchronizer>.Instance);
    }

    private static string Calendar(params string[] events) => $$"""
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//HomeAutomationNetDaemon.Tests//EN
        {{string.Join(Environment.NewLine, events)}}
        END:VCALENDAR
        """;

    private static string LocalEvent(
        DateTime date,
        int startHour,
        int startMinute,
        int endHour,
        int endMinute,
        string? extraProperty = null,
        string uid = "event")
    {
        var day = date.ToString("yyyyMMdd");
        return $$"""
            BEGIN:VEVENT
            UID:{{uid}}
            DTSTAMP:20260101T000000Z
            DTSTART;TZID=Europe/Warsaw:{{day}}T{{startHour:00}}{{startMinute:00}}00
            DTEND;TZID=Europe/Warsaw:{{day}}T{{endHour:00}}{{endMinute:00}}00
            {{extraProperty}}
            END:VEVENT
            """;
    }

    private static string Event(DateTime startUtc, DateTime endUtc) => $$"""
        BEGIN:VEVENT
        UID:utc-event
        DTSTAMP:20260101T000000Z
        DTSTART:{{startUtc:yyyyMMdd'T'HHmmss'Z'}}
        DTEND:{{endUtc:yyyyMMdd'T'HHmmss'Z'}}
        END:VEVENT
        """;

    private sealed class StubCalendarServer : IHttpClientFactory
    {
        private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _responses = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _requestCounts = new(StringComparer.Ordinal);

        public int TotalRequestCount => _requestCounts.Values.Sum();

        public StubCalendarServer Respond(string url, string content)
        {
            Add(url, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
            return this;
        }

        public StubCalendarServer Fail(string url)
        {
            Add(url, () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            return this;
        }

        public int RequestCount(string url) => _requestCounts.GetValueOrDefault(url);

        public HttpClient CreateClient(string name) => new(new Handler(this));

        private void Add(string url, Func<HttpResponseMessage> response)
        {
            if (!_responses.TryGetValue(url, out var queue))
            {
                queue = new Queue<Func<HttpResponseMessage>>();
                _responses[url] = queue;
            }

            queue.Enqueue(response);
        }

        private HttpResponseMessage Send(HttpRequestMessage request)
        {
            var url = request.RequestUri!.AbsoluteUri;
            _requestCounts[url] = RequestCount(url) + 1;
            if (!_responses.TryGetValue(url, out var queue) || queue.Count == 0)
            {
                throw new InvalidOperationException($"No stub response configured for {url}");
            }

            return queue.Dequeue().Invoke();
        }

        private sealed class Handler(StubCalendarServer server) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) => Task.FromResult(server.Send(request));
        }
    }
}
