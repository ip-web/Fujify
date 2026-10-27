using Avalonia;
using System;
using Fujify.Common;
using Fujify.Services;

namespace Fujify;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var parsed = CommandLineArgs.Parse(args);
        string channel = parsed.IsQuickProcess ? SingleInstanceService.ChannelQuick : SingleInstanceService.ChannelMain;

        if (OperatingSystem.IsWindows())
        {
            if (!SingleInstanceService.TryAcquireMutex(channel))
            {
                // Another instance of Fujify is already running on this channel.
                // Forward files to the running instance via IPC and exit immediately.
                SingleInstanceService.SendPayloadToExistingInstance(new IpcPayload
                {
                    Mode = parsed.Mode.ToString().ToLowerInvariant(),
                    Files = parsed.Files
                }, channel);
                return;
            }

            // Primary instance: start Named Pipe listener
            SingleInstanceService.StartServer(payload =>
            {
                App.HandleIncomingPayload(payload);
            }, channel);
        }

        ConfigureCrashLogging();

        try
        {
            App.InitialCommandLineArgs = parsed;
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            if (OperatingSystem.IsWindows())
            {
                SingleInstanceService.Release(channel);
            }
        }
    }

    private static void ConfigureCrashLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            LogFatalException("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            LogFatalException("TaskScheduler.UnobservedTaskException", e.Exception);
        };
    }

    private static void LogFatalException(string source, Exception? ex)
    {
        try
        {
            string logDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Fujifier");
            System.IO.Directory.CreateDirectory(logDir);
            string logFile = System.IO.Path.Combine(logDir, "crash.log");

            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FATAL ({source}):\n{ex?.ToString() ?? "Unknown exception"}\n\n";
            System.IO.File.AppendAllText(logFile, entry);
        }
        catch
        {
            // Suppress secondary crash logging errors
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
