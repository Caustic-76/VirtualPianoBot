using System.Threading;
using Avalonia;
using VirtualPianoBot.Services;

namespace VirtualPianoBot;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            AppServices.Initialize();

            AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            {
                try
                {
                    var exception = eventArgs.ExceptionObject as Exception
                        ?? new Exception($"Unhandled non-Exception object: {eventArgs.ExceptionObject}");
                    AppServices.Logger.Error($"Unhandled AppDomain exception. IsTerminating={eventArgs.IsTerminating}.", exception);
                    AppServices.Logger.Flush();
                }
                catch { }
            };

            TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            {
                try
                {
                    AppServices.Logger.Error("Unobserved Task exception.", eventArgs.Exception);
                    eventArgs.SetObserved();
                    AppServices.Logger.Flush();
                }
                catch { }
            };

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try
                {
                    AppServices.Logger.Info("ProcessExit received.");
                    AppServices.Logger.Flush();
                }
                catch { }
            };

            AppServices.Logger.Info("Global crash/exception diagnostics installed.");
            AppServices.Logger.Info("Virtual Piano Bot 6 starting.");
            AppServices.Logger.Info($"Application directory: {AppContext.BaseDirectory}");
            AppServices.Logger.Info($"Persistent data directory: {AppServices.Paths.DataDirectory}");

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            try
            {
                AppServices.Logger.Error("Fatal application startup error.", exception);
            }
            catch
            {
                // Last-resort handling must not throw another exception.
            }

            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
