using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace FourteenPlayer;

public partial class App : System.Windows.Application
{
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            // Register explicit AppUserModelID for Windows 10/11 Shell & SMTC Media Overlay
            SetCurrentProcessExplicitAppUserModelID("FourteenPlayer");

            // Ensure Start Menu shortcut exists so Windows SMTC display name & icon show "FourteenPlayer"
            EnsureStartMenuShortcut();
        }
        catch { }

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

    private static void EnsureStartMenuShortcut()
    {
        try
        {
            string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs");
            if (!Directory.Exists(startMenuDir)) Directory.CreateDirectory(startMenuDir);

            string shortcutPath = Path.Combine(startMenuDir, "FourteenPlayer.lnk");
            string currentExe = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe)) return;

            string iconPath = Path.Combine(AppContext.BaseDirectory, "app_icon.ico");
            if (!File.Exists(iconPath)) iconPath = currentExe;

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = currentExe;
                    shortcut.WorkingDirectory = AppContext.BaseDirectory;
                    shortcut.Description = "FourteenPlayer - Музыкальный плеер для Windows";
                    shortcut.IconLocation = iconPath + ",0";
                    shortcut.Save();
                }
            }
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
    }
}
