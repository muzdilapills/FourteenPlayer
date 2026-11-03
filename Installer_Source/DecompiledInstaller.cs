using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: CompilationRelaxations(8)]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
[assembly: Debuggable(DebuggableAttribute.DebuggingModes.IgnoreSymbolStoreSequencePoints)]
[assembly: TargetFramework(".NETCoreApp,Version=v9.0", FrameworkDisplayName = ".NET 9.0")]
[assembly: AssemblyCompany("FourteenPlayer_v2.0.release_Setup")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyFileVersion("2.0.0.0")]
[assembly: AssemblyInformationalVersion("2.0.0")]
[assembly: AssemblyProduct("FourteenPlayer Setup")]
[assembly: AssemblyTitle("FourteenPlayer_v2.0.release_Setup")]
[assembly: TargetPlatform("Windows7.0")]
[assembly: SupportedOSPlatform("Windows7.0")]
[assembly: AssemblyVersion("2.0.0.0")]
[module: RefSafetyRules(11)]
[CompilerGenerated]
internal static class ApplicationConfiguration
{
	public static void Initialize()
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		Application.SetHighDpiMode(HighDpiMode.SystemAware);
	}
}
namespace FourteenPlayer.Installer;

internal static class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		ApplicationConfiguration.Initialize();
		bool flag = false;
		foreach (string text in args)
		{
			if (text.Equals("/S", StringComparison.OrdinalIgnoreCase) || text.Equals("/SILENT", StringComparison.OrdinalIgnoreCase))
			{
				flag = true;
			}
		}
		if (flag)
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
			InstallCore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FourteenPlayer"), createDesktopShortcut: true, createStartMenuShortcut: true);
		}
		catch
		{
		}
	}

	public static void InstallCore(string targetDir, bool createDesktopShortcut, bool createStartMenuShortcut)
	{
		try
		{
			Process[] processesByName = Process.GetProcessesByName("FourteenPlayer");
			foreach (Process process in processesByName)
			{
				try
				{
					process.Kill();
					process.WaitForExit(1500);
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		if (!Directory.Exists(targetDir))
		{
			Directory.CreateDirectory(targetDir);
		}
		using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FourteenPlayer.Installer.app.zip"))
		{
			if (stream == null)
			{
				throw new FileNotFoundException("Внутренний ресурс app.zip не найден в инсталляторе.");
			}
			using ZipArchive zipArchive = new ZipArchive(stream, ZipArchiveMode.Read);
			foreach (ZipArchiveEntry entry in zipArchive.Entries)
			{
				if (string.IsNullOrEmpty(entry.Name))
				{
					string path = Path.Combine(targetDir, entry.FullName);
					if (!Directory.Exists(path))
					{
						Directory.CreateDirectory(path);
					}
					continue;
				}
				string text = Path.Combine(targetDir, entry.FullName);
				string directoryName = Path.GetDirectoryName(text);
				if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
				{
					Directory.CreateDirectory(directoryName);
				}
				entry.ExtractToFile(text, overwrite: true);
			}
		}
		string text2 = Path.Combine(targetDir, "FourteenPlayer.exe");
		string iconPath = Path.Combine(targetDir, "app_icon.ico");
		if (createDesktopShortcut)
		{
			CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "FourteenPlayer.lnk"), text2, iconPath, "FourteenPlayer - Музыкальный плеер");
		}
		if (createStartMenuShortcut)
		{
			string text3 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
			if (!Directory.Exists(text3))
			{
				Directory.CreateDirectory(text3);
			}
			CreateShortcut(Path.Combine(text3, "FourteenPlayer.lnk"), text2, iconPath, "FourteenPlayer - Музыкальный плеер");
		}
		CreateUninstaller(targetDir);
		RegisterUninstallInfo(targetDir, text2, iconPath);
	}

	private static void CreateShortcut(string shortcutPath, string targetPath, string iconPath, string description)
	{
		try
		{
			Type typeFromProgID = Type.GetTypeFromProgID("WScript.Shell");
			if (!(typeFromProgID != null))
			{
				return;
			}
			dynamic val = Activator.CreateInstance(typeFromProgID);
			if (val != null)
			{
				dynamic val2 = val.CreateShortcut(shortcutPath);
				val2.TargetPath = targetPath;
				val2.WorkingDirectory = Path.GetDirectoryName(targetPath);
				val2.Description = description;
				if (File.Exists(iconPath))
				{
					val2.IconLocation = iconPath + ",0";
				}
				val2.Save();
			}
		}
		catch
		{
		}
	}

	private static void CreateUninstaller(string targetDir)
	{
		try
		{
			string path = Path.Combine(targetDir, "Uninstall.bat");
			string value = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "FourteenPlayer.lnk");
			string value2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "FourteenPlayer.lnk");
			string contents = $"@echo off\ntitle Удаление FourteenPlayer\ntaskkill /F /IM FourteenPlayer.exe >nul 2>&1\ntimeout /t 1 /nobreak >nul\nif exist \"{value}\" del /f /q \"{value}\"\nif exist \"{value2}\" del /f /q \"{value2}\"\nreg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\FourteenPlayer\" /f >nul 2>&1\necho FourteenPlayer успешно удален.\ntimeout /t 2 /nobreak >nul\n(goto) 2>nul & rd /s /q \"{targetDir}\"\n";
			File.WriteAllText(path, contents, Encoding.Default);
		}
		catch
		{
		}
	}

	private static void RegisterUninstallInfo(string targetDir, string exePath, string iconPath)
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\FourteenPlayer");
			if (registryKey != null)
			{
				registryKey.SetValue("DisplayName", "FourteenPlayer");
				registryKey.SetValue("DisplayVersion", "2.0.release");
				registryKey.SetValue("Publisher", "muzdilapills");
				registryKey.SetValue("InstallLocation", targetDir);
				registryKey.SetValue("DisplayIcon", iconPath);
				registryKey.SetValue("UninstallString", "\"" + Path.Combine(targetDir, "Uninstall.bat") + "\"");
				registryKey.SetValue("URLInfoAbout", "https://github.com/muzdilapills/FourteenPlayer/releases");
				registryKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
				registryKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
			}
		}
		catch
		{
		}
	}
}
public class InstallerForm : Form
{
	private TextBox _txtPath;

	private CheckBox _chkDesktop;

	private CheckBox _chkStartMenu;

	private CheckBox _chkLaunch;

	private ProgressBar _progressBar;

	private Label _lblStatus;

	private Button _btnInstall;

	private Button _btnBrowse;

	public InstallerForm()
	{
		InitializeComponent();
	}

	private void InitializeComponent()
	{
		this.Text = "Установка FourteenPlayer v2.0.release";
		base.Size = new System.Drawing.Size(500, 390);
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
		base.MaximizeBox = false;
		this.BackColor = System.Drawing.Color.FromArgb(17, 20, 29);
		this.ForeColor = System.Drawing.Color.FromArgb(243, 244, 246);
		this.Font = new System.Drawing.Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Regular);
		try
		{
			using System.IO.Stream stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("FourteenPlayer.Installer.app_icon.ico");
			if (stream != null)
			{
				base.Icon = new System.Drawing.Icon(stream);
			}
		}
		catch
		{
		}
		System.Windows.Forms.Panel panel = new System.Windows.Forms.Panel
		{
			Dock = System.Windows.Forms.DockStyle.Top,
			Height = 72,
			BackColor = System.Drawing.Color.FromArgb(24, 29, 42),
			Padding = new System.Windows.Forms.Padding(18, 12, 18, 12)
		};
		System.Windows.Forms.Label value = new System.Windows.Forms.Label
		{
			Text = "FourteenPlayer v2.0.release",
			Font = new System.Drawing.Font("Segoe UI", 13f, System.Drawing.FontStyle.Bold),
			ForeColor = System.Drawing.Color.FromArgb(16, 185, 129),
			AutoSize = true,
			Location = new System.Drawing.Point(16, 12)
		};
		System.Windows.Forms.Label value2 = new System.Windows.Forms.Label
		{
			Text = "Мастер быстрой установки и обновления FourteenPlayer для Windows",
			Font = new System.Drawing.Font("Segoe UI", 8.5f, System.Drawing.FontStyle.Regular),
			ForeColor = System.Drawing.Color.FromArgb(156, 163, 175),
			AutoSize = true,
			Location = new System.Drawing.Point(18, 38)
		};
		panel.Controls.Add(value);
		panel.Controls.Add(value2);
		base.Controls.Add(panel);
		int num = 90;
		System.Windows.Forms.Label value3 = new System.Windows.Forms.Label
		{
			Text = "Папка установки:",
			Location = new System.Drawing.Point(20, num),
			AutoSize = true,
			ForeColor = System.Drawing.Color.FromArgb(209, 213, 219)
		};
		base.Controls.Add(value3);
		num += 24;
		string text = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Programs", "FourteenPlayer");
		this._txtPath = new System.Windows.Forms.TextBox
		{
			Text = text,
			Location = new System.Drawing.Point(20, num),
			Width = 345,
			BackColor = System.Drawing.Color.FromArgb(28, 34, 49),
			ForeColor = System.Drawing.Color.White,
			BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle
		};
		base.Controls.Add(this._txtPath);
		this._btnBrowse = new System.Windows.Forms.Button
		{
			Text = "Обзор...",
			Location = new System.Drawing.Point(375, num - 2),
			Width = 90,
			Height = 27,
			BackColor = System.Drawing.Color.FromArgb(37, 44, 61),
			ForeColor = System.Drawing.Color.FromArgb(243, 244, 246),
			FlatStyle = System.Windows.Forms.FlatStyle.Flat,
			Cursor = System.Windows.Forms.Cursors.Hand
		};
		this._btnBrowse.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(59, 70, 94);
		this._btnBrowse.Click += new System.EventHandler(this.BtnBrowse_Click);
		base.Controls.Add(this._btnBrowse);
		num += 38;
		this._chkDesktop = new System.Windows.Forms.CheckBox
		{
			Text = "Создать ярлык на Рабочем столе",
			Checked = true,
			Location = new System.Drawing.Point(22, num),
			AutoSize = true,
			ForeColor = System.Drawing.Color.FromArgb(209, 213, 219),
			Cursor = System.Windows.Forms.Cursors.Hand
		};
		base.Controls.Add(this._chkDesktop);
		num += 26;
		this._chkStartMenu = new System.Windows.Forms.CheckBox
		{
			Text = "Создать ярлык в меню «Пуск»",
			Checked = true,
			Location = new System.Drawing.Point(22, num),
			AutoSize = true,
			ForeColor = System.Drawing.Color.FromArgb(209, 213, 219),
			Cursor = System.Windows.Forms.Cursors.Hand
		};
		base.Controls.Add(this._chkStartMenu);
		num += 26;
		this._chkLaunch = new System.Windows.Forms.CheckBox
		{
			Text = "Запустить FourteenPlayer после установки",
			Checked = true,
			Location = new System.Drawing.Point(22, num),
			AutoSize = true,
			ForeColor = System.Drawing.Color.FromArgb(16, 185, 129),
			Cursor = System.Windows.Forms.Cursors.Hand
		};
		base.Controls.Add(this._chkLaunch);
		num += 36;
		this._progressBar = new System.Windows.Forms.ProgressBar
		{
			Location = new System.Drawing.Point(20, num),
			Width = 445,
			Height = 14,
			Style = System.Windows.Forms.ProgressBarStyle.Continuous,
			Value = 0,
			Visible = false
		};
		base.Controls.Add(this._progressBar);
		num += 20;
		this._lblStatus = new System.Windows.Forms.Label
		{
			Text = "Готов к установке или обновлению.",
			Location = new System.Drawing.Point(20, num),
			Width = 445,
			Height = 20,
			ForeColor = System.Drawing.Color.FromArgb(156, 163, 175),
			Font = new System.Drawing.Font("Segoe UI", 8.5f, System.Drawing.FontStyle.Regular)
		};
		base.Controls.Add(this._lblStatus);
		this._btnInstall = new System.Windows.Forms.Button
		{
			Text = "Установить",
			Location = new System.Drawing.Point(345, 305),
			Width = 120,
			Height = 34,
			BackColor = System.Drawing.Color.FromArgb(16, 185, 129),
			ForeColor = System.Drawing.Color.FromArgb(10, 25, 20),
			Font = new System.Drawing.Font("Segoe UI", 10f, System.Drawing.FontStyle.Bold),
			FlatStyle = System.Windows.Forms.FlatStyle.Flat,
			Cursor = System.Windows.Forms.Cursors.Hand
		};
		this._btnInstall.FlatAppearance.BorderSize = 0;
		this._btnInstall.Click += new System.EventHandler(this.BtnInstall_Click);
		base.Controls.Add(this._btnInstall);
	}

	private void BtnBrowse_Click(object? sender, EventArgs e)
	{
		FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
		{
			Description = "Выберите папку для установки FourteenPlayer",
			SelectedPath = _txtPath.Text
		};
		try
		{
			if (folderBrowserDialog.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(folderBrowserDialog.SelectedPath))
			{
				_txtPath.Text = Path.Combine(folderBrowserDialog.SelectedPath, "FourteenPlayer");
			}
		}
		finally
		{
			((IDisposable)(object)folderBrowserDialog)?.Dispose();
		}
	}

	private async void BtnInstall_Click(object? sender, EventArgs e)
	{
		if (_btnInstall.Text == "Завершить")
		{
			if (_chkLaunch.Checked)
			{
				string text = Path.Combine(_txtPath.Text.Trim(), "FourteenPlayer.exe");
				if (File.Exists(text))
				{
					try
					{
						Process.Start(new ProcessStartInfo
						{
							FileName = text,
							UseShellExecute = true
						});
					}
					catch
					{
					}
				}
			}
			Close();
			return;
		}
		string targetDir = _txtPath.Text.Trim();
		if (string.IsNullOrWhiteSpace(targetDir))
		{
			MessageBox.Show("Пожалуйста, укажите путь к папке установки.", "FourteenPlayer", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return;
		}
		_btnInstall.Enabled = false;
		_btnBrowse.Enabled = false;
		_txtPath.Enabled = false;
		_chkDesktop.Enabled = false;
		_chkStartMenu.Enabled = false;
		_progressBar.Visible = true;
		_progressBar.Value = 30;
		_lblStatus.Text = "Распаковка и установка компонентов...";
		try
		{
			await Task.Run(() =>
			{
				Program.InstallCore(targetDir, _chkDesktop.Checked, _chkStartMenu.Checked);
			});
			_progressBar.Value = 100;
			_lblStatus.Text = "Установка успешно завершена!";
			_lblStatus.ForeColor = Color.FromArgb(16, 185, 129);
			_btnInstall.Text = "Завершить";
			_btnInstall.Enabled = true;
		}
		catch (Exception ex)
		{
			_progressBar.Visible = false;
			_lblStatus.Text = "Ошибка установки: " + ex.Message;
			_lblStatus.ForeColor = Color.FromArgb(239, 68, 68);
			_btnInstall.Enabled = true;
			_btnBrowse.Enabled = true;
			_txtPath.Enabled = true;
		}
	}
}
