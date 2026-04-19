using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace EasyEyes;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EasyEyes",
        "EasyEyes.log");

    private const long MaxLogFileSize = 1024 * 1024; // 1 MB

    private static Mutex? s_instanceMutex;
    private static StreamWriter? s_logWriter;

    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        s_instanceMutex = new Mutex(true, "EasyEyes-C6A214F5-8E3B-4B2A-9F1D-7A5E6C3D2B1A", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Easy Eyes is already running.", "Easy Eyes",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);

        try
        {
            TruncateLogIfOversized();
            s_logWriter = new StreamWriter(LogPath, append: true) { AutoFlush = true };
        }
        catch
        {
            // Best-effort logging — continue without file logging
        }

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
        s_logWriter?.Dispose();
        s_instanceMutex?.ReleaseMutex();
        s_instanceMutex?.Dispose();
        base.OnExit(e);
    }

    public static void Log(string message)
    {
        try
        {
            s_logWriter?.WriteLine($"{DateTime.Now:u} {message}");
        }
        catch
        {
            // Best-effort logging
        }
    }

    private static void TruncateLogIfOversized()
    {
        var info = new FileInfo(LogPath);
        if (info.Exists && info.Length > MaxLogFileSize)
        {
            File.Delete(LogPath);
        }
    }
}
