using System;
using System.IO;
using System.Windows;

namespace FourteenPlayer;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            try
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), ev.ExceptionObject?.ToString() + Environment.NewLine);
            }
            catch { }
        };
        DispatcherUnhandledException += (s, ev) =>
        {
            try
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), ev.Exception?.ToString() + Environment.NewLine);
            }
            catch { }
        };
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "exit_reason.log"),
                $"ExitCode: {e.ApplicationExitCode}\nStackTrace:\n{Environment.StackTrace}\n");
        }
        catch { }
        base.OnExit(e);
    }
}
