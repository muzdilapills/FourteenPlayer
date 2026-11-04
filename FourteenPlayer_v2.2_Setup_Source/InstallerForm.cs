using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FourteenPlayer.Installer;

public class InstallerForm : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    private TextBox _txtPath = null!;
    private CheckBox _chkDesktop = null!;
    private CheckBox _chkStartMenu = null!;
    private CheckBox _chkLaunch = null!;
    private ModernDarkProgressBar _progressBar = null!;
    private Label _lblStatus = null!;
    private Button _btnInstall = null!;
    private Button _btnBrowse = null!;

    public InstallerForm()
    {
        InitializeComponent();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int darkMode = 1;
            DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkMode, sizeof(int));
            int captionColor = ColorTranslator.ToWin32(Color.FromArgb(24, 29, 42)); // Match header panel #181D2A
            DwmSetWindowAttribute(Handle, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
            int textColor = ColorTranslator.ToWin32(Color.FromArgb(243, 244, 246));
            DwmSetWindowAttribute(Handle, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
        }
        catch { }
    }

    private void InitializeComponent()
    {
        Text = "Установка FourteenPlayer v2.2";
        Size = new Size(500, 395);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = Color.FromArgb(17, 20, 29);
        ForeColor = Color.FromArgb(243, 244, 246);
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FourteenPlayer.Installer.app_icon.ico");
            if (stream != null)
            {
                Icon = new Icon(stream);
            }
        }
        catch { }

        // 1. Dark Top Header (No white background)
        Panel headerPanel = new()
        {
            Dock = DockStyle.Top,
            Height = 74,
            BackColor = Color.FromArgb(24, 29, 42),
            Padding = new Padding(18, 12, 18, 12)
        };

        Label lblTitle = new()
        {
            Text = "FourteenPlayer v2.2",
            Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(16, 185, 129),
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(18, 12)
        };

        Label lblSub = new()
        {
            Text = "Чистый и быстрый плеер для Windows",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(156, 163, 175),
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(20, 39)
        };

        headerPanel.Controls.Add(lblTitle);
        headerPanel.Controls.Add(lblSub);
        Controls.Add(headerPanel);

        // 2. Body controls
        int curY = 92;

        Label lblPath = new()
        {
            Text = "Путь установки:",
            Location = new Point(20, curY),
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(209, 213, 219)
        };
        Controls.Add(lblPath);

        curY += 24;
        string defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "FourteenPlayer"
        );

        _txtPath = new TextBox
        {
            Text = defaultPath,
            Location = new Point(20, curY),
            Width = 345,
            BackColor = Color.FromArgb(28, 34, 49),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        Controls.Add(_txtPath);

        _btnBrowse = new Button
        {
            Text = "Обзор...",
            Location = new Point(375, curY - 2),
            Width = 90,
            Height = 27,
            BackColor = Color.FromArgb(37, 44, 61),
            ForeColor = Color.FromArgb(243, 244, 246),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnBrowse.FlatAppearance.BorderColor = Color.FromArgb(59, 70, 94);
        _btnBrowse.Click += BtnBrowse_Click;
        Controls.Add(_btnBrowse);

        curY += 38;
        _chkDesktop = new CheckBox
        {
            Text = "Создать ярлык на рабочем столе",
            Checked = true,
            Location = new Point(22, curY),
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(209, 213, 219),
            Cursor = Cursors.Hand
        };
        Controls.Add(_chkDesktop);

        curY += 26;
        _chkStartMenu = new CheckBox
        {
            Text = "Создать ярлык в меню «Пуск»",
            Checked = true,
            Location = new Point(22, curY),
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(209, 213, 219),
            Cursor = Cursors.Hand
        };
        Controls.Add(_chkStartMenu);

        curY += 26;
        _chkLaunch = new CheckBox
        {
            Text = "Запустить FourteenPlayer после установки",
            Checked = true,
            Location = new Point(22, curY),
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(16, 185, 129),
            Cursor = Cursors.Hand
        };
        Controls.Add(_chkLaunch);

        curY += 36;
        _progressBar = new ModernDarkProgressBar
        {
            Location = new Point(20, curY),
            Width = 445,
            Height = 10,
            Value = 0,
            Visible = false
        };
        Controls.Add(_progressBar);

        curY += 18;
        _lblStatus = new Label
        {
            Text = "Готов к установке и обновлению.",
            Location = new Point(20, curY),
            Width = 445,
            Height = 22,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(156, 163, 175),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
        };
        Controls.Add(_lblStatus);

        _btnInstall = new Button
        {
            Text = "Установить",
            Location = new Point(345, 308),
            Width = 120,
            Height = 34,
            BackColor = Color.FromArgb(16, 185, 129),
            ForeColor = Color.FromArgb(10, 25, 20),
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnInstall.FlatAppearance.BorderSize = 0;
        _btnInstall.Click += BtnInstall_Click;
        Controls.Add(_btnInstall);
    }

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        using FolderBrowserDialog fbd = new()
        {
            Description = "Выберите папку для установки FourteenPlayer",
            SelectedPath = _txtPath.Text
        };

        if (fbd.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
        {
            _txtPath.Text = Path.Combine(fbd.SelectedPath, "FourteenPlayer");
        }
    }

    private async void BtnInstall_Click(object? sender, EventArgs e)
    {
        if (_btnInstall.Text == "Запустить")
        {
            if (_chkLaunch.Checked)
            {
                string exePath = Path.Combine(_txtPath.Text.Trim(), "FourteenPlayer.exe");
                if (File.Exists(exePath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = exePath,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
            }
            Close();
            return;
        }

        string targetDir = _txtPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            MessageBox.Show("Пожалуйста, укажите путь к папке установки.", "FourteenPlayer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnInstall.Enabled = false;
        _btnBrowse.Enabled = false;
        _txtPath.Enabled = false;
        _chkDesktop.Enabled = false;
        _chkStartMenu.Enabled = false;

        _progressBar.Visible = true;
        _progressBar.Value = 30;
        _lblStatus.Text = "Копирование файлов и обновление...";

        try
        {
            bool desktop = _chkDesktop.Checked;
            bool startMenu = _chkStartMenu.Checked;

            await Task.Run(() =>
            {
                Program.InstallCore(targetDir, desktop, startMenu);
            });

            _progressBar.Value = 100;
            _lblStatus.Text = "FourteenPlayer v2.2 успешно установлен!";
            _lblStatus.ForeColor = Color.FromArgb(16, 185, 129);
            _btnInstall.Text = "Запустить";
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

public class ModernDarkProgressBar : Control
{
    private int _value;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 100);
            Invalidate();
        }
    }

    public ModernDarkProgressBar()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        Height = 10;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var bgBrush = new SolidBrush(Color.FromArgb(28, 34, 49));
        e.Graphics.FillRectangle(bgBrush, ClientRectangle);

        if (_value > 0)
        {
            int fillWidth = (int)((ClientRectangle.Width * _value) / 100.0);
            using var fillBrush = new SolidBrush(Color.FromArgb(16, 185, 129));
            e.Graphics.FillRectangle(fillBrush, 0, 0, fillWidth, Height);
        }

        using var borderPen = new Pen(Color.FromArgb(45, 53, 72));
        e.Graphics.DrawRectangle(borderPen, 0, 0, ClientRectangle.Width - 1, ClientRectangle.Height - 1);
    }
}