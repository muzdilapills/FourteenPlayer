using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace FourteenPlayer;

public class SmtcAudioEngine : IDisposable
{
    private readonly MediaPlayer _player;
    private readonly SystemMediaTransportControls _smtc;
    private readonly Action _onPlayPause;
    private readonly Action _onNext;
    private readonly Action _onPrev;
    private readonly Action _onStop;
    private readonly Action<TimeSpan> _onMediaOpened;
    private readonly Action _onMediaEnded;
    private readonly Action<string> _onMediaFailed;

    private RandomAccessStreamReference? _iconStreamRef;
    private bool _isDisposed;
    private TrackItem? _currentTrack;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    private const byte VK_VOLUME_UP = 0xAF;
    private const byte VK_VOLUME_DOWN = 0xAE;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public SmtcAudioEngine(
        Action onPlayPause,
        Action onNext,
        Action onPrev,
        Action onStop,
        Action<TimeSpan> onMediaOpened,
        Action onMediaEnded,
        Action<string> onMediaFailed)
    {
        _onPlayPause = onPlayPause;
        _onNext = onNext;
        _onPrev = onPrev;
        _onStop = onStop;
        _onMediaOpened = onMediaOpened;
        _onMediaEnded = onMediaEnded;
        _onMediaFailed = onMediaFailed;

        _player = new MediaPlayer();
        _player.AudioCategory = MediaPlayerAudioCategory.Media;
        // Disable automatic CommandManager so MediaPlayer does NOT wipe out manual DisplayUpdater metadata
        _player.CommandManager.IsEnabled = false;

        _smtc = _player.SystemMediaTransportControls;
        _smtc.IsEnabled = true;
        _smtc.IsPlayEnabled = true;
        _smtc.IsPauseEnabled = true;
        _smtc.IsNextEnabled = true;
        _smtc.IsPreviousEnabled = true;
        _smtc.IsStopEnabled = true;
        _smtc.ButtonPressed += Smtc_ButtonPressed;

        _player.MediaOpened += (s, e) =>
        {
            var duration = _player.PlaybackSession.NaturalDuration;
            if (_currentTrack != null)
            {
                UpdateOverlay(_currentTrack, _smtc.PlaybackStatus == MediaPlaybackStatus.Playing);
            }
            _onMediaOpened(duration);
        };

        _player.MediaEnded += (s, e) =>
        {
            _onMediaEnded();
        };

        _player.MediaFailed += (s, e) =>
        {
            _onMediaFailed(e.ErrorMessage ?? "Ошибка воспроизведения аудиофайла");
        };

        _ = InitAppIconAsync();
    }

    private async Task InitAppIconAsync()
    {
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "app_icon.png");
            if (File.Exists(iconPath))
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(iconPath);
                _iconStreamRef = RandomAccessStreamReference.CreateFromFile(storageFile);
                if (_currentTrack != null)
                {
                    UpdateOverlay(_currentTrack, _smtc.PlaybackStatus == MediaPlaybackStatus.Playing);
                }
            }
        }
        catch { }
    }

    private void Smtc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        try
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                case SystemMediaTransportControlsButton.Pause:
                    _onPlayPause();
                    break;
                case SystemMediaTransportControlsButton.Next:
                    _onNext();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    _onPrev();
                    break;
                case SystemMediaTransportControlsButton.Stop:
                    _onStop();
                    break;
            }
        }
        catch { }
    }

    private MediaPlaybackItem CreatePlaybackItem(TrackItem track)
    {
        var uri = new Uri(track.FilePath);
        var source = MediaSource.CreateFromUri(uri);
        var item = new MediaPlaybackItem(source);

        try
        {
            var props = item.GetDisplayProperties();
            props.Type = MediaPlaybackType.Music;
            props.MusicProperties.Title = string.IsNullOrWhiteSpace(track.Title)
                ? Path.GetFileNameWithoutExtension(track.FilePath)
                : track.Title;
            props.MusicProperties.Artist = string.IsNullOrWhiteSpace(track.Artist)
                ? "FourteenPlayer"
                : track.Artist;
            props.MusicProperties.AlbumTitle = string.IsNullOrWhiteSpace(track.Album)
                ? "FourteenPlayer"
                : track.Album;

            if (_iconStreamRef != null)
            {
                props.Thumbnail = _iconStreamRef;
            }

            item.ApplyDisplayProperties(props);
        }
        catch { }

        return item;
    }

    public void OpenAndPlay(TrackItem track)
    {
        if (_isDisposed) return;
        _currentTrack = track;

        try
        {
            var item = CreatePlaybackItem(track);
            _player.Source = item;
            _player.Play();
            _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
            UpdateOverlay(track, isPlaying: true);
        }
        catch (Exception ex)
        {
            _onMediaFailed(ex.Message);
        }
    }

    public void Open(TrackItem track)
    {
        if (_isDisposed) return;
        _currentTrack = track;

        try
        {
            var item = CreatePlaybackItem(track);
            _player.Source = item;
            _smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
            UpdateOverlay(track, isPlaying: false);
        }
        catch (Exception ex)
        {
            _onMediaFailed(ex.Message);
        }
    }

    public void Play()
    {
        if (_isDisposed) return;
        _player.Play();
        _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
        if (_currentTrack != null)
        {
            UpdateOverlay(_currentTrack, true);
        }
    }

    public void Pause()
    {
        if (_isDisposed) return;
        _player.Pause();
        _smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
        if (_currentTrack != null)
        {
            UpdateOverlay(_currentTrack, false);
        }
    }

    public void Stop()
    {
        if (_isDisposed) return;
        _player.Pause();
        _player.PlaybackSession.Position = TimeSpan.Zero;
        _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
    }

    public TimeSpan Position
    {
        get => _player.PlaybackSession.Position;
        set
        {
            if (!_isDisposed)
            {
                _player.PlaybackSession.Position = value;
            }
        }
    }

    public TimeSpan Duration => _player.PlaybackSession.NaturalDuration;

    public double Volume
    {
        get => _player.Volume;
        set
        {
            if (!_isDisposed)
            {
                _player.Volume = Math.Clamp(value, 0.0, 1.0);
            }
        }
    }

    public bool IsMuted
    {
        get => _player.IsMuted;
        set
        {
            if (!_isDisposed)
            {
                _player.IsMuted = value;
            }
        }
    }

    public void UpdateOverlay(TrackItem? track, bool isPlaying)
    {
        if (_isDisposed) return;
        try
        {
            if (track == null)
            {
                _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
                return;
            }

            var updater = _smtc.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = string.IsNullOrWhiteSpace(track.Title)
                ? Path.GetFileNameWithoutExtension(track.FilePath)
                : track.Title;
            updater.MusicProperties.Artist = string.IsNullOrWhiteSpace(track.Artist)
                ? "FourteenPlayer"
                : track.Artist;
            updater.MusicProperties.AlbumTitle = string.IsNullOrWhiteSpace(track.Album)
                ? "FourteenPlayer"
                : track.Album;

            if (_iconStreamRef != null)
            {
                updater.Thumbnail = _iconStreamRef;
            }

            updater.Update();
            _smtc.PlaybackStatus = isPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
        }
        catch { }
    }

    public void UpdateStatus(bool isPlaying)
    {
        if (_isDisposed) return;
        try
        {
            _smtc.PlaybackStatus = isPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
        }
        catch { }
    }

    public void TriggerWindowsOverlay()
    {
        try
        {
            // Rapidly pulsate Volume Up / Volume Down keys to display the stylish Windows SMTC overlay immediately!
            keybd_event(VK_VOLUME_UP, 0, 0, UIntPtr.Zero);
            keybd_event(VK_VOLUME_UP, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_VOLUME_DOWN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_VOLUME_DOWN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch { }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
            _smtc.IsEnabled = false;
            _player.Dispose();
        }
        catch { }
    }
}
