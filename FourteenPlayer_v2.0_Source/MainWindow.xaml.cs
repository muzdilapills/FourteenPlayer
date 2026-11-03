using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using WpfButton = System.Windows.Controls.Button;
using WpfCursors = System.Windows.Input.Cursors;
using WpfBrushes = System.Windows.Media.Brushes;
using Drawing = System.Drawing;
using DrawingIcon = System.Drawing.SystemIcons;

namespace FourteenPlayer;

public partial class MainWindow : Window
{
    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _playbackTimer = new();
    private readonly DispatcherTimer _perfTimer = new();

    private readonly ObservableCollection<TrackItem> _allTracks = new();
    private readonly ObservableCollection<TrackItem> _displayTracks = new();

    private Forms.NotifyIcon? _notifyIcon;
    private PlayerSettings _settings = new();
    private SmtcManager? _smtcManager;

    private int _currentTrackIndex = -1;
    private bool _isPlaying = false;
    private bool _isDraggingSeek = false;
    private double _previousVolume = 0.7;
    private bool _isMuted = false;
    private bool _isShuffle = false;
    
    // Repeat Modes: 0 = Off, 1 = Repeat All (Список), 2 = Repeat Album (Альбом), 3 = Repeat One (Трек)
    private int _repeatMode = 1;
    private readonly Random _random = new();

    private bool _isExplicitExit = false;

    // Track being renamed
    private TrackItem? _renamingTrack;

    // Update Checker
    private enum UpdateCheckState { Checking, UpToDate, UpdateAvailable, Error }
    private UpdateCheckState _updateState = UpdateCheckState.Checking;
    private const string WebsiteReleasesUrl = "https://github.com/muzdilapills/FourteenPlayer/releases";
    private const string CurrentAppVersion = "2.0.0";

    // CPU Performance tracking
    private readonly Stopwatch _cpuStopwatch = Stopwatch.StartNew();
    private TimeSpan _lastCpuTime = TimeSpan.Zero;
    private double _smoothedCpu = 0.0;

    public MainWindow()
    {
        InitializeComponent();

        try
        {
            var iconUri = new Uri("pack://application:,,,/app_icon.ico", UriKind.RelativeOrAbsolute);
            this.Icon = BitmapFrame.Create(iconUri);
        }
        catch { }

        PlaylistListBox.ItemsSource = _displayTracks;

        // Configure MediaPlayer
        _player.MediaOpened += Player_MediaOpened;
        _player.MediaEnded += Player_MediaEnded;
        _player.MediaFailed += Player_MediaFailed;

        // Configure Playback Timer (fast 100ms for smooth counter)
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(100);
        _playbackTimer.Tick += PlaybackTimer_Tick;

        // Configure CPU Performance Monitor Timer (every 2.5s)
        _perfTimer.Interval = TimeSpan.FromMilliseconds(2500);
        _perfTimer.Tick += PerfTimer_Tick;
        _perfTimer.Start();

        // Initialize System Tray Icon
        InitSystemTray();

        // Initialize CPU measurement baseline
        try
        {
            using var proc = Process.GetCurrentProcess();
            _lastCpuTime = proc.TotalProcessorTime;
            _cpuStopwatch.Restart();
        }
        catch { }

        // Load Settings & Saved Playlist
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void InitSmtcAsync()
    {
        Task.Run(() =>
        {
            try
            {
                var smtc = new SmtcManager(
                    onPlayPause: () => Dispatcher.Invoke(TogglePlayPause),
                    onNext: () => Dispatcher.Invoke(PlayNextTrack),
                    onPrev: () => Dispatcher.Invoke(PlayPreviousTrack),
                    onStop: () => Dispatcher.Invoke(StopPlayback)
                );
                _smtcManager = smtc;
                Dispatcher.Invoke(() =>
                {
                    if (_currentTrackIndex >= 0 && _currentTrackIndex < _allTracks.Count)
                    {
                        _smtcManager?.UpdateTrack(_allTracks[_currentTrackIndex], _isPlaying);
                    }
                });
            }
            catch { }
        });
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = PlayerSettings.Load();

        // Restore Volume
        _previousVolume = _settings.Volume > 0 ? _settings.Volume : 0.7;
        _isMuted = _settings.IsMuted;
        _isShuffle = _settings.IsShuffle;
        _repeatMode = Math.Clamp(_settings.RepeatMode, 0, 3);

        VolumeSlider.Value = _isMuted ? 0 : _previousVolume;
        _player.Volume = VolumeSlider.Value;

        UpdateShuffleButtonUI();
        UpdateRepeatButtonUI();

        // Restore Playlist
        if (_settings.PlaylistFiles.Count > 0)
        {
            var existingFiles = _settings.PlaylistFiles.Where(File.Exists).ToList();
            if (existingFiles.Count > 0)
            {
                AddFilesToPlaylist(existingFiles, autoPlay: false);

                if (_settings.LastTrackIndex >= 0 && _settings.LastTrackIndex < _allTracks.Count)
                {
                    SelectTrack(_settings.LastTrackIndex);
                }
            }
        }

        UpdatePerformanceStats();

        // Start checking for updates in background
        _ = CheckForUpdatesAsync();

        // Initialize SMTC in background
        InitSmtcAsync();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // When closing the application, minimize to tray unless explicit exit
        if (!_isExplicitExit)
        {
            e.Cancel = true;
            Hide();
            if (_notifyIcon != null)
            {
                _notifyIcon.ShowBalloonTip(1500, "FourteenPlayer", "Приложение свернуто в трей. Музыка продолжает играть.", Forms.ToolTipIcon.Info);
            }
            return;
        }

        SaveCurrentSettings();

        _playbackTimer.Stop();
        _perfTimer.Stop();

        _player.Stop();
        _player.Close();

        _smtcManager?.Dispose();
        _smtcManager = null;

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }

    private void SaveCurrentSettings()
    {
        _settings.Volume = _isMuted ? _previousVolume : VolumeSlider.Value;
        _settings.IsMuted = _isMuted;
        _settings.IsShuffle = _isShuffle;
        _settings.RepeatMode = _repeatMode;
        _settings.LastTrackIndex = _currentTrackIndex;
        _settings.PlaylistFiles = _allTracks.Select(t => t.FilePath).ToList();
        _settings.Save();
    }

    #region Update Checker

    private async Task CheckForUpdatesAsync()
    {
        _updateState = UpdateCheckState.Checking;
        TxtStatusBarRight.Text = "Проверка обновления";
        TxtStatusBarRight.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x9C, 0xA3, 0xAF));
        BtnStatusUpdate.Cursor = WpfCursors.Arrow;
        BtnStatusUpdate.ToolTip = "Идет проверка обновлений на GitHub...";

        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(7);
            client.DefaultRequestHeaders.Add("User-Agent", "FourteenPlayer-Client");

            string apiUrl = "https://api.github.com/repos/muzdilapills/FourteenPlayer/releases/latest";
            var response = await client.GetAsync(apiUrl);

            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tagName = "";
                if (root.TryGetProperty("tag_name", out var tagProp))
                {
                    tagName = tagProp.GetString() ?? "";
                }

                bool hasNewer = IsVersionNewer(tagName, CurrentAppVersion);

                Dispatcher.Invoke(() =>
                {
                    if (hasNewer)
                    {
                        _updateState = UpdateCheckState.UpdateAvailable;
                        TxtStatusBarRight.Text = "Вышло обновление! Попробуешь?";
                        TxtStatusBarRight.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x10, 0xB9, 0x81));
                        BtnStatusUpdate.Cursor = WpfCursors.Hand;
                        BtnStatusUpdate.ToolTip = $"Доступна новая версия {tagName}! Нажмите, чтобы открыть страницу загрузки.";
                    }
                    else
                    {
                        _updateState = UpdateCheckState.UpToDate;
                        TxtStatusBarRight.Text = "Все класс, у вас новая версия!";
                        TxtStatusBarRight.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x10, 0xB9, 0x81));
                        BtnStatusUpdate.Cursor = WpfCursors.Arrow;
                        BtnStatusUpdate.ToolTip = "У вас установлена последняя актуальная версия.";
                    }
                });
            }
            else
            {
                Dispatcher.Invoke(SetUpdateCheckError);
            }
        }
        catch
        {
            Dispatcher.Invoke(SetUpdateCheckError);
        }
    }

    private void SetUpdateCheckError()
    {
        _updateState = UpdateCheckState.Error;
        TxtStatusBarRight.Text = "Что-то пошло не так";
        TxtStatusBarRight.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xEF, 0x44, 0x44));
        BtnStatusUpdate.Cursor = WpfCursors.Hand;
        BtnStatusUpdate.ToolTip = "Не удалось проверить обновление. Нажмите здесь, чтобы перезапустить приложение.";
    }

    private static bool IsVersionNewer(string remoteTag, string currentVer)
    {
        if (string.IsNullOrWhiteSpace(remoteTag)) return false;

        // Clean version strings (e.g. "v2.1" -> "2.1", "v2.0.beta" -> "2.0")
        string cleanRemote = remoteTag.Trim().TrimStart('v', 'V');
        int dashIdx = cleanRemote.IndexOf('-');
        if (dashIdx > 0) cleanRemote = cleanRemote.Substring(0, dashIdx);

        string cleanCurrent = currentVer.Trim().TrimStart('v', 'V');
        dashIdx = cleanCurrent.IndexOf('-');
        if (dashIdx > 0) cleanCurrent = cleanCurrent.Substring(0, dashIdx);

        if (Version.TryParse(cleanRemote, out var rVer) && Version.TryParse(cleanCurrent, out var cVer))
        {
            return rVer > cVer;
        }

        return false;
    }

    private void BtnStatusUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updateState == UpdateCheckState.UpdateAvailable)
        {
            OpenUrl(WebsiteReleasesUrl);
        }
        else if (_updateState == UpdateCheckState.Error)
        {
            RestartApplication();
        }
    }

    private void RestartApplication()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });
            }

            _isExplicitExit = true;
            System.Windows.Application.Current.Shutdown();
        }
        catch { }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    #endregion

    #region Settings & Version History & Rename Overlays

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        VersionHistoryOverlay.Visibility = Visibility.Collapsed;
        RenameModalOverlay.Visibility = Visibility.Collapsed;
        SettingsOverlay.Visibility = Visibility.Visible;
    }

    private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private void SettingsBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private void BtnTabControls_Click(object sender, MouseButtonEventArgs e)
    {
        SwitchSettingsTab(isAbout: false);
    }

    private void BtnTabAbout_Click(object sender, MouseButtonEventArgs e)
    {
        SwitchSettingsTab(isAbout: true);
    }

    private void SwitchSettingsTab(bool isAbout)
    {
        if (isAbout)
        {
            TabContentControls.Visibility = Visibility.Collapsed;
            TabContentAbout.Visibility = Visibility.Visible;

            TabBorderControls.Background = WpfBrushes.Transparent;
            TabBorderControls.BorderBrush = WpfBrushes.Transparent;
            TxtTabControls.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x9C, 0xA3, 0xAF));

            TabBorderAbout.Background = new SolidColorBrush(MediaColor.FromRgb(0x1C, 0x24, 0x36));
            TabBorderAbout.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0x10, 0xB9, 0x81));
            TxtTabAbout.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x10, 0xB9, 0x81));
        }
        else
        {
            TabContentControls.Visibility = Visibility.Visible;
            TabContentAbout.Visibility = Visibility.Collapsed;

            TabBorderControls.Background = new SolidColorBrush(MediaColor.FromRgb(0x1C, 0x24, 0x36));
            TabBorderControls.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0x10, 0xB9, 0x81));
            TxtTabControls.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x10, 0xB9, 0x81));

            TabBorderAbout.Background = WpfBrushes.Transparent;
            TabBorderAbout.BorderBrush = WpfBrushes.Transparent;
            TxtTabAbout.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x9C, 0xA3, 0xAF));
        }
    }

    private void BtnWebsiteLink_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(WebsiteReleasesUrl);
    }

    private void BtnVersionBadge_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
        RenameModalOverlay.Visibility = Visibility.Collapsed;
        VersionHistoryOverlay.Visibility = Visibility.Visible;
    }

    private void BtnCloseVersionHistory_Click(object sender, RoutedEventArgs e)
    {
        VersionHistoryOverlay.Visibility = Visibility.Collapsed;
    }

    private void VersionHistoryBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        VersionHistoryOverlay.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region Rename File on Disk Functionality

    private void BtnRenameTrack_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton btn && btn.DataContext is TrackItem track)
        {
            OpenRenameDialog(track);
        }
    }

    private void ContextRename_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistListBox.SelectedItem is TrackItem track)
        {
            OpenRenameDialog(track);
        }
    }

    private void OpenRenameDialog(TrackItem track)
    {
        if (string.IsNullOrEmpty(track.FilePath) || !File.Exists(track.FilePath))
        {
            Forms.MessageBox.Show("Файл не найден на диске: " + track.FilePath, "FourteenPlayer", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning);
            return;
        }

        _renamingTrack = track;
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(track.FilePath);
        string ext = Path.GetExtension(track.FilePath);

        TxtRenameInput.Text = fileNameWithoutExt;
        TxtRenameExtension.Text = ext;
        TxtRenameError.Text = "";

        SettingsOverlay.Visibility = Visibility.Collapsed;
        VersionHistoryOverlay.Visibility = Visibility.Collapsed;
        RenameModalOverlay.Visibility = Visibility.Visible;

        TxtRenameInput.Focus();
        TxtRenameInput.SelectAll();
    }

    private void BtnCloseRename_Click(object sender, RoutedEventArgs e)
    {
        RenameModalOverlay.Visibility = Visibility.Collapsed;
        _renamingTrack = null;
    }

    private void RenameBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        RenameModalOverlay.Visibility = Visibility.Collapsed;
        _renamingTrack = null;
    }

    private void TxtRenameInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            BtnSaveRename_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            BtnCloseRename_Click(sender, e);
            e.Handled = true;
        }
    }

    private void BtnSaveRename_Click(object sender, RoutedEventArgs e)
    {
        if (_renamingTrack == null)
        {
            RenameModalOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        string newName = TxtRenameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            TxtRenameError.Text = "Имя файла не может быть пустым.";
            return;
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();
        if (newName.IndexOfAny(invalidChars) >= 0)
        {
            TxtRenameError.Text = "Имя содержит недопустимые символы: \\ / : * ? \" < > |";
            return;
        }

        string oldPath = _renamingTrack.FilePath;
        string ext = Path.GetExtension(oldPath);
        string dir = Path.GetDirectoryName(oldPath) ?? "";
        string newFileName = newName + ext;
        string newPath = Path.Combine(dir, newFileName);

        // If name didn't change at all
        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            _renamingTrack.Title = newName;
            RenameModalOverlay.Visibility = Visibility.Collapsed;
            _renamingTrack = null;
            return;
        }

        // Check if destination file already exists
        if (File.Exists(newPath))
        {
            TxtRenameError.Text = "Файл с таким именем уже существует в папке!";
            return;
        }

        try
        {
            int trackIndex = _allTracks.IndexOf(_renamingTrack);
            bool isCurrentTrack = (trackIndex == _currentTrackIndex);

            // If the track is currently loaded/playing, release file lock
            TimeSpan savedPosition = TimeSpan.Zero;
            bool wasPlaying = _isPlaying;

            if (isCurrentTrack)
            {
                savedPosition = _player.Position;
                _player.Stop();
                _player.Close();
            }

            // Move / rename file on disk
            File.Move(oldPath, newPath);

            // Update track item
            _renamingTrack.FilePath = newPath;
            _renamingTrack.Title = newName;

            // Reopen if it was current track
            if (isCurrentTrack)
            {
                var newUri = new Uri(newPath);
                _player.Open(newUri);
                _player.Position = savedPosition;

                if (wasPlaying)
                {
                    _player.Play();
                    _isPlaying = true;
                    TxtPlayPauseIcon.Text = "⏸";
                    _playbackTimer.Start();
                }

                TxtNowPlayingTitle.Text = newName;
                UpdateTrayTooltip(newName);
                _smtcManager?.UpdateTrack(_renamingTrack, _isPlaying);
            }

            SaveCurrentSettings();
            ApplyFilter();

            RenameModalOverlay.Visibility = Visibility.Collapsed;
            _renamingTrack = null;
        }
        catch (Exception ex)
        {
            TxtRenameError.Text = "Ошибка переименования: " + ex.Message;
        }
    }

    private void ContextPlay_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistListBox.SelectedItem is TrackItem track)
        {
            int idx = _allTracks.IndexOf(track);
            if (idx >= 0) PlayTrack(idx);
        }
    }

    private void ContextOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistListBox.SelectedItem is TrackItem track && File.Exists(track.FilePath))
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{track.FilePath}\"");
            }
            catch { }
        }
    }

    private void ContextRemove_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistListBox.SelectedItem is TrackItem track)
        {
            RemoveTrack(track);
        }
    }

    #endregion

    #region System Tray Integration

    private void InitSystemTray()
    {
        try
        {
            Drawing.Icon? appTrayIcon = null;
            try
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_icon.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    appTrayIcon = new Drawing.Icon(iconPath);
                }
                else
                {
                    var exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                    {
                        appTrayIcon = Drawing.Icon.ExtractAssociatedIcon(exePath);
                    }
                }
            }
            catch { }

            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = appTrayIcon ?? DrawingIcon.Application,
                Text = "FourteenPlayer v2.0",
                Visible = true
            };

            var contextMenu = new Forms.ContextMenuStrip();
            contextMenu.Items.Add("🗔 Открыть Player", null, (s, e) => ShowAndRestoreWindow());
            contextMenu.Items.Add(new Forms.ToolStripSeparator());
            contextMenu.Items.Add("❌ Выход", null, (s, e) => {
                _isExplicitExit = true;
                _smtcManager?.Dispose();
                _smtcManager = null;
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }
                Close();
                System.Windows.Application.Current.Shutdown();
            });

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (s, e) => ShowAndRestoreWindow();
            _notifyIcon.Click += (s, e) => {
                if (e is Forms.MouseEventArgs me && me.Button == Forms.MouseButtons.Left)
                {
                    ShowAndRestoreWindow();
                }
            };
        }
        catch { }
    }

    private void ShowAndRestoreWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void UpdateTrayTooltip(string trackName)
    {
        if (_notifyIcon != null)
        {
            string text = $"FourteenPlayer: {trackName}";
            if (text.Length >= 64) text = text.Substring(0, 60) + "...";
            _notifyIcon.Text = text;
        }
    }

    #endregion

    #region Realtime CPU & Memory Monitor

    private void PerfTimer_Tick(object? sender, EventArgs e)
    {
        UpdatePerformanceStats();
    }

    private void UpdatePerformanceStats()
    {
        try
        {
            using var proc = Process.GetCurrentProcess();
            var curCpuTime = proc.TotalProcessorTime;
            var elapsedMs = _cpuStopwatch.ElapsedMilliseconds;

            long memoryMB = proc.WorkingSet64 / (1024 * 1024);

            if (_lastCpuTime == TimeSpan.Zero)
            {
                _lastCpuTime = curCpuTime;
                _cpuStopwatch.Restart();
                TxtPerfStats.Text = $"⚡ ОЗУ: ~{memoryMB} МБ | ЦП: 0.0%";
                return;
            }

            if (elapsedMs < 500)
            {
                TxtPerfStats.Text = $"⚡ ОЗУ: ~{memoryMB} МБ | ЦП: {_smoothedCpu.ToString("0.0", CultureInfo.InvariantCulture)}%";
                return;
            }

            var cpuUsedMs = Math.Max(0.0, (curCpuTime - _lastCpuTime).TotalMilliseconds);
            _lastCpuTime = curCpuTime;
            _cpuStopwatch.Restart();

            var totalMsPassed = elapsedMs * Environment.ProcessorCount;
            double rawCpuPercent = totalMsPassed > 0 ? (cpuUsedMs / totalMsPassed) * 100.0 : 0.0;
            rawCpuPercent = Math.Clamp(rawCpuPercent, 0.0, 100.0);

            // Responsive smoothing to avoid jitter/spikes
            _smoothedCpu = _smoothedCpu == 0.0 ? rawCpuPercent : (_smoothedCpu * 0.3 + rawCpuPercent * 0.7);
            if (_smoothedCpu < 0.05) _smoothedCpu = 0.0;

            TxtPerfStats.Text = $"⚡ ОЗУ: ~{memoryMB} МБ | ЦП: {_smoothedCpu.ToString("0.0", CultureInfo.InvariantCulture)}%";
        }
        catch { }
    }

    #endregion

    #region Player Events

    private void Player_MediaOpened(object? sender, EventArgs e)
    {
        if (_player.NaturalDuration.HasTimeSpan)
        {
            var duration = _player.NaturalDuration.TimeSpan;
            SeekSlider.Maximum = Math.Max(1.0, duration.TotalSeconds);
            TxtTotalTime.Text = FormatTime(duration);

            if (_currentTrackIndex >= 0 && _currentTrackIndex < _allTracks.Count)
            {
                _allTracks[_currentTrackIndex].Duration = duration;
            }
        }

        if (_isPlaying)
        {
            _player.Play();
            _playbackTimer.Start();
        }
    }

    private void Player_MediaEnded(object? sender, EventArgs e)
    {
        // Mode 3: Repeat One (Текущий трек)
        if (_repeatMode == 3)
        {
            _player.Position = TimeSpan.Zero;
            _player.Play();
            return;
        }

        // Mode 2: Repeat Album (Текущий альбом/папка)
        if (_repeatMode == 2 && _allTracks.Count > 0 && _currentTrackIndex >= 0)
        {
            string currentAlbum = _allTracks[_currentTrackIndex].Album;
            var albumIndices = _allTracks
                .Select((track, idx) => (track, idx))
                .Where(x => string.Equals(x.track.Album, currentAlbum, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.idx)
                .ToList();

            if (albumIndices.Count > 0)
            {
                if (_isShuffle && albumIndices.Count > 1)
                {
                    int nextIdx;
                    do
                    {
                        nextIdx = albumIndices[_random.Next(albumIndices.Count)];
                    } while (nextIdx == _currentTrackIndex && albumIndices.Count > 1);
                    PlayTrack(nextIdx);
                    return;
                }

                int posInAlbum = albumIndices.IndexOf(_currentTrackIndex);
                int nextAlbumTrackIndex = albumIndices[(posInAlbum + 1) % albumIndices.Count];
                PlayTrack(nextAlbumTrackIndex);
                return;
            }
        }

        // Mode 1: Repeat All (Весь список) or Mode 0 (Без повтора)
        PlayNextTrack();
    }

    private void Player_MediaFailed(object? sender, ExceptionEventArgs e)
    {
        TxtNowPlayingArtist.Text = "Ошибка загрузки аудиофайла";
        _isPlaying = false;
        TxtPlayPauseIcon.Text = "▶";
        _playbackTimer.Stop();
        _smtcManager?.UpdateStatus(false);
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isDraggingSeek && _isPlaying && _player.NaturalDuration.HasTimeSpan)
        {
            var pos = _player.Position;
            SeekSlider.Value = pos.TotalSeconds;
            TxtCurrentTime.Text = FormatTime(pos);
        }
    }

    #endregion

    #region Playback Control Logic

    private void PlayTrack(int index)
    {
        if (index < 0 || index >= _allTracks.Count) return;

        // Reset previous track playing status
        if (_currentTrackIndex >= 0 && _currentTrackIndex < _allTracks.Count)
        {
            _allTracks[_currentTrackIndex].IsPlaying = false;
        }

        _currentTrackIndex = index;
        var track = _allTracks[_currentTrackIndex];
        track.IsPlaying = true;

        TxtNowPlayingTitle.Text = track.Title;
        TxtNowPlayingArtist.Text = track.DisplaySubtitle;

        UpdateTrayTooltip(track.Title);

        _playbackTimer.Stop();
        SeekSlider.Value = 0;
        TxtCurrentTime.Text = "0:00";

        if (track.Duration > TimeSpan.Zero)
        {
            SeekSlider.Maximum = Math.Max(1.0, track.Duration.TotalSeconds);
            TxtTotalTime.Text = track.DurationString;
        }
        else
        {
            SeekSlider.Maximum = 100;
            TxtTotalTime.Text = "--:--";
        }

        try
        {
            var uri = new Uri(track.FilePath);
            if (_player.Source != uri)
            {
                _player.Stop();
                _player.Close();
                _player.Open(uri);
            }
            _player.Play();
            _isPlaying = true;
            TxtPlayPauseIcon.Text = "⏸";
            _playbackTimer.Start();

            // Notify Windows SMTC Overlay
            _smtcManager?.UpdateTrack(track, true);
        }
        catch (Exception ex)
        {
            TxtNowPlayingArtist.Text = "Не удалось открыть: " + ex.Message;
        }
    }

    private void SelectTrack(int index)
    {
        if (index < 0 || index >= _allTracks.Count) return;
        _currentTrackIndex = index;
        var track = _allTracks[_currentTrackIndex];
        TxtNowPlayingTitle.Text = track.Title;
        TxtNowPlayingArtist.Text = track.DisplaySubtitle;

        if (track.Duration > TimeSpan.Zero)
        {
            SeekSlider.Maximum = Math.Max(1.0, track.Duration.TotalSeconds);
            TxtTotalTime.Text = track.DurationString;
        }

        try
        {
            var uri = new Uri(track.FilePath);
            if (_player.Source != uri)
            {
                _player.Stop();
                _player.Close();
                _player.Open(uri);
            }
            _smtcManager?.UpdateTrack(track, false);
        }
        catch { }
    }

    private void TogglePlayPause()
    {
        if (_allTracks.Count == 0) return;

        if (_isPlaying)
        {
            _player.Pause();
            _isPlaying = false;
            TxtPlayPauseIcon.Text = "▶";
            _playbackTimer.Stop();
            _smtcManager?.UpdateStatus(false);
        }
        else
        {
            if (_currentTrackIndex < 0 || _player.Source == null)
            {
                int target = _currentTrackIndex >= 0 ? _currentTrackIndex : 0;
                PlayTrack(target);
            }
            else
            {
                _player.Play();
                _isPlaying = true;
                TxtPlayPauseIcon.Text = "⏸";
                _playbackTimer.Start();
                _smtcManager?.UpdateStatus(true);
            }
        }
    }

    private void StopPlayback()
    {
        _player.Stop();
        _player.Position = TimeSpan.Zero;
        _isPlaying = false;
        TxtPlayPauseIcon.Text = "▶";
        SeekSlider.Value = 0;
        TxtCurrentTime.Text = "0:00";
        _playbackTimer.Stop();

        if (_currentTrackIndex >= 0 && _currentTrackIndex < _allTracks.Count)
        {
            _allTracks[_currentTrackIndex].IsPlaying = false;
        }

        _smtcManager?.UpdateStatus(false);
    }

    private void PlayNextTrack()
    {
        if (_allTracks.Count == 0) return;

        // If repeat album mode is active
        if (_repeatMode == 2 && _currentTrackIndex >= 0)
        {
            string currentAlbum = _allTracks[_currentTrackIndex].Album;
            var albumIndices = _allTracks
                .Select((track, idx) => (track, idx))
                .Where(x => string.Equals(x.track.Album, currentAlbum, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.idx)
                .ToList();

            if (albumIndices.Count > 1)
            {
                if (_isShuffle)
                {
                    int nextIdx;
                    do
                    {
                        nextIdx = albumIndices[_random.Next(albumIndices.Count)];
                    } while (nextIdx == _currentTrackIndex);
                    PlayTrack(nextIdx);
                    return;
                }

                int posInAlbum = albumIndices.IndexOf(_currentTrackIndex);
                int nextIndex = albumIndices[(posInAlbum + 1) % albumIndices.Count];
                PlayTrack(nextIndex);
                return;
            }
        }

        // Shuffle mode across playlist
        if (_isShuffle && _allTracks.Count > 1)
        {
            int next;
            do
            {
                next = _random.Next(_allTracks.Count);
            } while (next == _currentTrackIndex && _allTracks.Count > 1);
            PlayTrack(next);
            return;
        }

        int targetIndex = _currentTrackIndex + 1;
        if (targetIndex >= _allTracks.Count)
        {
            if (_repeatMode == 1)
            {
                targetIndex = 0;
            }
            else if (_repeatMode == 2 || _repeatMode == 3)
            {
                targetIndex = _currentTrackIndex >= 0 ? _currentTrackIndex : 0;
            }
            else
            {
                StopPlayback();
                return;
            }
        }

        PlayTrack(targetIndex);
    }

    private void PlayPreviousTrack()
    {
        if (_allTracks.Count == 0) return;

        if (_player.Position.TotalSeconds > 3)
        {
            _player.Position = TimeSpan.Zero;
            SeekSlider.Value = 0;
            TxtCurrentTime.Text = "0:00";
            return;
        }

        if (_repeatMode == 2 && _currentTrackIndex >= 0)
        {
            string currentAlbum = _allTracks[_currentTrackIndex].Album;
            var albumIndices = _allTracks
                .Select((track, idx) => (track, idx))
                .Where(x => string.Equals(x.track.Album, currentAlbum, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.idx)
                .ToList();

            if (albumIndices.Count > 1)
            {
                int posInAlbum = albumIndices.IndexOf(_currentTrackIndex);
                int prevIndex = albumIndices[(posInAlbum - 1 + albumIndices.Count) % albumIndices.Count];
                PlayTrack(prevIndex);
                return;
            }
        }

        int targetIndex = _currentTrackIndex - 1;
        if (targetIndex < 0)
        {
            targetIndex = _allTracks.Count - 1;
        }

        PlayTrack(targetIndex);
    }

    #endregion

    #region Playlist & File Handling

    public void AddFilesToPlaylist(IEnumerable<string> filePaths, bool autoPlay = true)
    {
        bool wasEmpty = _allTracks.Count == 0;

        foreach (var path in filePaths)
        {
            if (!MetadataHelper.IsSupportedAudioFile(path)) continue;
            if (_allTracks.Any(t => t.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;

            var (title, artist, album) = MetadataHelper.ReadBasicTags(path);
            var item = new TrackItem
            {
                TrackNumber = _allTracks.Count + 1,
                FilePath = path,
                Title = title,
                Artist = artist,
                Album = album
            };
            _allTracks.Add(item);
        }

        ApplyFilter();
        UpdateTrackCountLabel();

        if (wasEmpty && autoPlay && _allTracks.Count > 0)
        {
            PlayTrack(0);
        }
    }

    private void ApplyFilter()
    {
        string query = TxtSearch.Text.Trim();
        _displayTracks.Clear();

        var matches = string.IsNullOrEmpty(query)
            ? _allTracks
            : _allTracks.Where(t => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                                 || t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase)
                                 || t.Album.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var track in matches)
        {
            _displayTracks.Add(track);
        }

        EmptyStatePanel.Visibility = _allTracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTrackCountLabel()
    {
        TxtTrackCount.Text = $"{_allTracks.Count} треков";
    }

    private void ReorderTrackNumbers()
    {
        for (int i = 0; i < _allTracks.Count; i++)
        {
            _allTracks[i].TrackNumber = i + 1;
        }
    }

    private void RemoveTrack(TrackItem track)
    {
        int index = _allTracks.IndexOf(track);
        if (index == _currentTrackIndex)
        {
            StopPlayback();
        }

        _allTracks.Remove(track);
        ReorderTrackNumbers();
        ApplyFilter();
        UpdateTrackCountLabel();
    }

    #endregion

    #region Realistic Button Active State Stylers

    private void UpdateShuffleButtonUI()
    {
        BtnShuffle.Tag = _isShuffle ? "Active" : "Normal";
        TxtShuffleIcon.Foreground = _isShuffle
            ? new SolidColorBrush(MediaColor.FromRgb(0x10, 0xB9, 0x81))
            : new SolidColorBrush(MediaColor.FromRgb(0x9C, 0xA3, 0xAF));

        BtnShuffle.ToolTip = _isShuffle ? "Случайный порядок: ВКЛЮЧЕН" : "Случайный порядок: ВЫКЛЮЧЕН";
    }

    private void UpdateRepeatButtonUI()
    {
        switch (_repeatMode)
        {
            case 0: // Off
                BtnRepeat.Tag = "Normal";
                TxtRepeatIcon.Text = "➡";
                TxtRepeatIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x9C, 0xA3, 0xAF));
                BtnRepeat.ToolTip = "Режим повтора: Без повтора (по порядку)";
                break;
            case 1: // Repeat All (Список)
                BtnRepeat.Tag = "Active";
                TxtRepeatIcon.Text = "🔁";
                TxtRepeatIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x10, 0xB9, 0x81));
                BtnRepeat.ToolTip = "Режим повтора: Весь список треков";
                break;
            case 2: // Repeat Album (Альбом)
                BtnRepeat.Tag = "Active";
                TxtRepeatIcon.Text = "💿";
                TxtRepeatIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x10, 0xB9, 0x81));
                BtnRepeat.ToolTip = "Режим повтора: Текущий альбом / папка";
                break;
            case 3: // Repeat One (Трек)
                BtnRepeat.Tag = "Active";
                TxtRepeatIcon.Text = "🔂";
                TxtRepeatIcon.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x10, 0xB9, 0x81));
                BtnRepeat.ToolTip = "Режим повтора: Один трек (зациклен)";
                break;
        }
    }

    #endregion

    #region UI Event Handlers

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnPlayPause_Click(object sender, RoutedEventArgs e) => TogglePlayPause();
    private void BtnStop_Click(object sender, RoutedEventArgs e) => StopPlayback();
    private void BtnNext_Click(object sender, RoutedEventArgs e) => PlayNextTrack();
    private void BtnPrev_Click(object sender, RoutedEventArgs e) => PlayPreviousTrack();

    private void BtnShuffle_Click(object sender, RoutedEventArgs e)
    {
        _isShuffle = !_isShuffle;
        UpdateShuffleButtonUI();
    }

    private void BtnRepeat_Click(object sender, RoutedEventArgs e)
    {
        _repeatMode = (_repeatMode + 1) % 4;
        UpdateRepeatButtonUI();
    }

    private void BtnMute_Click(object sender, RoutedEventArgs e)
    {
        if (_isMuted)
        {
            _isMuted = false;
            VolumeSlider.Value = _previousVolume > 0.05 ? _previousVolume : 0.7;
            _player.Volume = VolumeSlider.Value;
            TxtVolumeIcon.Text = "🔊";
        }
        else
        {
            _isMuted = true;
            _previousVolume = VolumeSlider.Value;
            VolumeSlider.Value = 0;
            _player.Volume = 0;
            TxtVolumeIcon.Text = "🔇";
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_player != null)
        {
            _player.Volume = e.NewValue;
            int percent = (int)(e.NewValue * 100);

            if (TxtVolumePercent != null)
                TxtVolumePercent.Text = $"{percent}%";

            if (percent == 0)
            {
                TxtVolumeIcon.Text = "🔇";
                _isMuted = true;
            }
            else
            {
                TxtVolumeIcon.Text = percent < 50 ? "🔉" : "🔊";
                _isMuted = false;
                _previousVolume = e.NewValue;
            }
        }
    }

    private void VolumeControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double step = e.Delta > 0 ? 0.05 : -0.05;
        VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + step, 0.0, 1.0);
        e.Handled = true;
    }

    private void SeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSeek = true;
    }

    private void SeekSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSeek = false;
        if (_player.NaturalDuration.HasTimeSpan)
        {
            _player.Position = TimeSpan.FromSeconds(SeekSlider.Value);
        }
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isDraggingSeek)
        {
            TxtCurrentTime.Text = FormatTime(TimeSpan.FromSeconds(e.NewValue));
        }
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtSearchPlaceholder != null)
            TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    private void BtnAddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Forms.OpenFileDialog
        {
            Title = "Выберите песни для воспроизведения",
            Filter = "Аудиофайлы (*.mp3;*.wav;*.flac;*.m4a;*.aac;*.wma;*.ogg)|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.wma;*.ogg|Все файлы (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            AddFilesToPlaylist(dialog.FileNames);
        }
    }

    private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Выберите папку с музыкой или альбом"
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK && Directory.Exists(dialog.SelectedPath))
        {
            try
            {
                var files = Directory.GetFiles(dialog.SelectedPath, "*.*", SearchOption.AllDirectories)
                    .Where(MetadataHelper.IsSupportedAudioFile);
                AddFilesToPlaylist(files);
            }
            catch (Exception ex)
            {
                Forms.MessageBox.Show("Ошибка при чтении папки: " + ex.Message);
            }
        }
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        _allTracks.Clear();
        _displayTracks.Clear();
        _currentTrackIndex = -1;
        TxtNowPlayingTitle.Text = "Плейлист пуст";
        TxtNowPlayingArtist.Text = "Добавьте песни или альбомы для воспроизведения";
        EmptyStatePanel.Visibility = Visibility.Visible;
        UpdateTrackCountLabel();
        _smtcManager?.UpdateTrack(null, false);
    }

    private void BtnRemoveTrack_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton btn && btn.DataContext is TrackItem track)
        {
            RemoveTrack(track);
        }
    }

    private void PlaylistListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlaylistListBox.SelectedItem is TrackItem track)
        {
            int index = _allTracks.IndexOf(track);
            if (index >= 0)
            {
                PlayTrack(index);
            }
        }
    }

    private void PlaylistListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlaylistListBox.SelectedIndex >= 0 && !_isPlaying)
        {
            SelectTrack(PlaylistListBox.SelectedIndex);
        }
    }

    private void PlaylistListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? dep = e.OriginalSource as DependencyObject;
        while (dep != null && dep is not ListBoxItem && dep != PlaylistListBox)
        {
            dep = VisualTreeHelper.GetParent(dep);
        }
        if (dep is ListBoxItem item)
        {
            item.IsSelected = true;
        }
    }

    private void Playlist_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Playlist_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            string[] items = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            var filesToAdd = new List<string>();

            foreach (var item in items)
            {
                if (File.Exists(item))
                {
                    filesToAdd.Add(item);
                }
                else if (Directory.Exists(item))
                {
                    try
                    {
                        var files = Directory.GetFiles(item, "*.*", SearchOption.AllDirectories)
                            .Where(MetadataHelper.IsSupportedAudioFile);
                        filesToAdd.AddRange(files);
                    }
                    catch { }
                }
            }

            if (filesToAdd.Count > 0)
            {
                AddFilesToPlaylist(filesToAdd);
            }
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (RenameModalOverlay.Visibility == Visibility.Visible)
            {
                BtnCloseRename_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (SettingsOverlay.Visibility == Visibility.Visible)
            {
                SettingsOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }
            if (VersionHistoryOverlay.Visibility == Visibility.Visible)
            {
                VersionHistoryOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }
        }

        if (TxtSearch.IsFocused || TxtRenameInput.IsFocused) return;

        switch (e.Key)
        {
            case Key.F2:
                if (PlaylistListBox.SelectedItem is TrackItem cur)
                {
                    OpenRenameDialog(cur);
                    e.Handled = true;
                }
                break;
            case Key.Enter:
                if (PlaylistListBox.SelectedItem is TrackItem selectedTrack)
                {
                    int idx = _allTracks.IndexOf(selectedTrack);
                    if (idx >= 0)
                    {
                        PlayTrack(idx);
                        e.Handled = true;
                    }
                }
                break;
            case Key.Space:
                TogglePlayPause();
                e.Handled = true;
                break;
            case Key.Left:
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    PlayPreviousTrack();
                }
                else if (_player.NaturalDuration.HasTimeSpan)
                {
                    _player.Position = TimeSpan.FromSeconds(Math.Max(0, _player.Position.TotalSeconds - 5));
                    SeekSlider.Value = _player.Position.TotalSeconds;
                    TxtCurrentTime.Text = FormatTime(_player.Position);
                }
                e.Handled = true;
                break;
            case Key.Right:
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    PlayNextTrack();
                }
                else if (_player.NaturalDuration.HasTimeSpan)
                {
                    _player.Position = TimeSpan.FromSeconds(Math.Min(SeekSlider.Maximum, _player.Position.TotalSeconds + 5));
                    SeekSlider.Value = _player.Position.TotalSeconds;
                    TxtCurrentTime.Text = FormatTime(_player.Position);
                }
                e.Handled = true;
                break;
            case Key.Up:
                VolumeSlider.Value = Math.Min(1.0, VolumeSlider.Value + 0.05);
                e.Handled = true;
                break;
            case Key.Down:
                VolumeSlider.Value = Math.Max(0.0, VolumeSlider.Value - 0.05);
                e.Handled = true;
                break;
            case Key.M:
                BtnMute_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.S:
                BtnShuffle_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.R:
                BtnRepeat_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Delete:
                if (PlaylistListBox.SelectedItem is TrackItem item)
                {
                    RemoveTrack(item);
                }
                e.Handled = true;
                break;
        }
    }

    #endregion

    private static string FormatTime(TimeSpan t)
    {
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }
}
