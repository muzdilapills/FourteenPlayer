using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FourteenPlayer.Installer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        bool silent = false;
        foreach (string arg in args)
        {
            if (arg.Equals("/S", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("/SILENT", StringComparison.OrdinalIgnoreCase))
            {
                silent = true;
            }
        }

        if (silent)
        {
            RunSilentInstall();
        }
        else
        {
            Application.Run(new InstallerForm());
        }
    }

    private static void RunSilentInstall()
    {
        try
        {
            string defaultDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "FourteenPlayer"
            );
            InstallCore(defaultDir, createDesktopShortcut: true, createStartMenuShortcut: true);
        }
        catch { }
    }

    public static void InstallCore(string targetDir, bool createDesktopShortcut, bool createStartMenuShortcut)
    {
        // 1. Terminate running player processes
        try
        {
            var processes = Process.GetProcessesByName("FourteenPlayer");
            foreach (var proc in processes)
            {
                try
                {
                    proc.Kill();
                    proc.WaitForExit(2000);
                }
                catch { }
            }
        }
        catch { }

        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        // 2. Extract embedded app.zip
        using (Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FourteenPlayer.Installer.app.zip"))
        {
            if (stream == null)
            {
                throw new FileNotFoundException("Встроенный архив app.zip не найден в инсталляторе.");
            }

            using ZipArchive archive = new(stream, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    string dirPath = Path.Combine(targetDir, entry.FullName);
                    if (!Directory.Exists(dirPath))
                    {
                        Directory.CreateDirectory(dirPath);
                    }
                    continue;
                }

                string fullPath = Path.Combine(targetDir, entry.FullName);
                string? parentDir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                entry.ExtractToFile(fullPath, overwrite: true);
            }
        }

        string exePath = Path.Combine(targetDir, "FourteenPlayer.exe");
        string iconPath = Path.Combine(targetDir, "app_icon.ico");

        // 3. Shortcuts
        if (createDesktopShortcut)
        {
            string desktopPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "FourteenPlayer.lnk"
            );
            CreateShortcut(desktopPath, exePath, iconPath, "FourteenPlayer - Музыкальный плеер");
        }

        if (createStartMenuShortcut)
        {
            string startMenuDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs"
            );
            if (!Directory.Exists(startMenuDir))
            {
                Directory.CreateDirectory(startMenuDir);
            }
            string startMenuPath = Path.Combine(startMenuDir, "FourteenPlayer.lnk");
            CreateShortcut(startMenuPath, exePath, iconPath, "FourteenPlayer - Музыкальный плеер");
        }

        // 4. Uninstaller & Registry
        CreateUninstaller(targetDir);
        RegisterUninstallInfo(targetDir, exePath, iconPath);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string iconPath, string description)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell != null)
            {
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                shortcut.Description = description;
                if (File.Exists(iconPath))
                {
                    shortcut.IconLocation = iconPath + ",0";
                }
                shortcut.Save();
            }
        }
        catch { }
    }

    private static void CreateUninstaller(string targetDir)
    {
        try
        {
            string uninstallerPath = Path.Combine(targetDir, "Uninstall.bat");
            string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "FourteenPlayer.lnk");
            string startLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "FourteenPlayer.lnk");

            string contents =
                "@echo off\r\n" +
                "title Удаление FourteenPlayer\r\n" +
                "taskkill /F /IM FourteenPlayer.exe >nul 2>&1\r\n" +
                "timeout /t 1 /nobreak >nul\r\n" +
                $"if exist \"{desktopLnk}\" del /f /q \"{desktopLnk}\"\r\n" +
                $"if exist \"{startLnk}\" del /f /q \"{startLnk}\"\r\n" +
                "reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\FourteenPlayer\" /f >nul 2>&1\r\n" +
                "echo FourteenPlayer успешно удален.\r\n" +
                "timeout /t 2 /nobreak >nul\r\n" +
                $"(goto) 2>nul & rd /s /q \"{targetDir}\"\r\n";

            File.WriteAllText(uninstallerPath, contents, Encoding.Default);
        }
        catch { }
    }

    private static void RegisterUninstallInfo(string targetDir, string exePath, string iconPath)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\FourteenPlayer");
            if (key != null)
            {
                key.SetValue("DisplayName", "FourteenPlayer v2.2");
                key.SetValue("DisplayVersion", "2.2.0");
                key.SetValue("Publisher", "muzdilapills");
                key.SetValue("InstallLocation", targetDir);
                key.SetValue("DisplayIcon", iconPath);
                key.SetValue("UninstallString", $"\"{Path.Combine(targetDir, "Uninstall.bat")}\"");
                key.SetValue("URLInfoAbout", "https://github.com/muzdilapills/FourteenPlayer/releases");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }
        catch { }
    }
}