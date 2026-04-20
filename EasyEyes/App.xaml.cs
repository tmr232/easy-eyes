using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace EasyEyes;

public partial class App : Application
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EasyEyes");

    private static readonly string LogPath = Path.Combine(LogDir, "EasyEyes.log");
    private static readonly string OldLogPath = Path.Combine(LogDir, "EasyEyes.old.log");

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

        Directory.CreateDirectory(LogDir);

        try
        {
            RotateLogIfOversized();
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

    public static void FatalError(string message, Exception ex)
    {
        Log($"FATAL: {message}: {ex}");
        MessageBox.Show(
            $"Easy Eyes encountered a fatal error and must close.\n\n{message}\n\n{ex.Message}",
            "Easy Eyes",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Current.Shutdown(1);
    }

    private static void RotateLogIfOversized()
    {
        var info = new FileInfo(LogPath);
        if (info.Exists && info.Length > MaxLogFileSize)
        {
            File.Move(LogPath, OldLogPath, overwrite: true);
        }
    }
}
