using Avalonia;

namespace VehicleMotionCues;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // One overlay per user session; a second launch just exits.
        using var mutex = new Mutex(true, @"Local\VehicleMotionCues.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
            return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
