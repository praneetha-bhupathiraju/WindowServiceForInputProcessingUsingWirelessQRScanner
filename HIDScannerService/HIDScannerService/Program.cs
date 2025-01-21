using HIDScannerService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService() // Enables running as a Windows Service
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders(); // Remove default providers
                logging.AddEventLog(settings =>
                {
                    settings.SourceName = "HIDScannerService"; // Set Windows Event Log source name
                });

                // Uncomment the following line for local console logging during debugging
                // logging.AddConsole(); // Enables console logging
            })
            .ConfigureServices(services =>
            {
                services.AddHostedService<Worker>(); // Registers the Worker service
            });
}
