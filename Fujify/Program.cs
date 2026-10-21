using Avalonia;
using System;

namespace Fujify;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!Services.SingleInstanceService.TryAcquireMutex())
            {
                // Another instance of Fujify is already running.
                // Forward files to the running instance via IPC and exit immediately.
                Services.SingleInstanceService.SendArgsToExistingInstance(args);
                return;
            }

            // Primary instance: start Named Pipe listener for files opened in Windows Explorer
            Services.SingleInstanceService.StartServer(files =>
            {
                App.HandleIncomingFiles(files);
            });
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            if (OperatingSystem.IsWindows())
            {
                Services.SingleInstanceService.Release();
            }
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
