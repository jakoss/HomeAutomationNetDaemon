using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.Hosting;
using NetDaemon.Extensions.Logging;
using NetDaemon.Extensions.Scheduler;
using NetDaemon.Extensions.Tts;
using NetDaemon.Runtime;
using HomeAssistantGenerated;
using HomeAutomationNetDaemon.Apps.WorkFromHome;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CA1812

var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
Console.WriteLine($"Starting NetDaemon {version}...");

try
{
    var app = Host.CreateDefaultBuilder(args)
        .UseNetDaemonAppSettings()
        .UseNetDaemonDefaultLogging()
        .UseNetDaemonRuntime()
        .UseNetDaemonTextToSpeech()
        .ConfigureServices((_, services) =>
            {
                services
                    .AddAppsFromAssembly(Assembly.GetExecutingAssembly())
                    .AddNetDaemonStateManager()
                    .AddNetDaemonScheduler()
                    .AddHomeAssistantGenerated()
                    .ConfigureHttpClientDefaults(builder =>
                    {
                        builder.ConfigureHttpClient(client =>
                        {
                            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; AcmeInc/1.0)");
                        });
                        builder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                        {
                            AllowAutoRedirect = false
                        });
                    });
                
                services.AddSingleton<CalendarSynchronizer>();
            }
        )
        .Build();
        
        Console.WriteLine("Starting host...");
        await app.RunAsync()
            .ConfigureAwait(false);
}
catch (Exception e)
{
    Console.WriteLine($"Failed to start host... {e}");
    throw;
}