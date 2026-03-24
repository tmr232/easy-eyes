using System;
using System.IO;
using System.Windows;

namespace EasyEyes;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EasyEyes",
        "EasyEyes.log");

    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);

        DispatcherUnhandledException += (_, args) =>
        {
            Log($"DispatcherUnhandledException: {args.Exception}");
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log($"UnhandledException: {args.ExceptionObject}");
        };

        Log("App started");
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log("App exiting");
        base.OnExit(e);
    }

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:u} {message}\r\n");
        }
        catch
        {
            // Best-effort logging
        }
    }
}
