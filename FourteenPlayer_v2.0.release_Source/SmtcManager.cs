using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace FourteenPlayer;

public class SmtcManager : IDisposable
{
    private readonly MediaPlayer _winMediaPlayer;
    private readonly SystemMediaTransportControls _smtc;
    private readonly Action _onPlayPause;
    private readonly Action _onNext;
    private readonly Action _onPrev;
    private readonly Action _onStop;
    private RandomAccessStreamReference? _iconStreamRef;
    private bool _isDisposed;

    public SmtcManager(Action onPlayPause, Action onNext, Action onPrev, Action onStop)
    {
        _onPlayPause = onPlayPause;
        _onNext = onNext;
        _onPrev = onPrev;
        _onStop = onStop;

        _winMediaPlayer = new MediaPlayer();
        _winMediaPlayer.CommandManager.IsEnabled = false;

        _smtc = _winMediaPlayer.SystemMediaTransportControls;
        _smtc.IsEnabled = true;
        _smtc.IsPlayEnabled = true;
        _smtc.IsPauseEnabled = true;
        _smtc.IsNextEnabled = true;
        _smtc.IsPreviousEnabled = true;
        _smtc.IsStopEnabled = true;

        _smtc.ButtonPressed += Smtc_ButtonPressed;

        _ = InitAppIconAsync();
    }

    private async Task InitAppIconAsync()
    {
        try
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_icon.png");
            if (File.Exists(iconPath))
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(iconPath);
                _iconStreamRef = RandomAccessStreamReference.CreateFromFile(storageFile);
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

    public void UpdateTrack(TrackItem? track, bool isPlaying)
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
            updater.MusicProperties.Title = string.IsNullOrWhiteSpace(track.Title) ? "Без названия" : track.Title;
            updater.MusicProperties.Artist = string.IsNullOrWhiteSpace(track.Artist) ? "FourteenPlayer" : track.Artist;
            updater.MusicProperties.AlbumTitle = string.IsNullOrWhiteSpace(track.Album) ? "" : track.Album;

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

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
            _smtc.IsEnabled = false;
            _winMediaPlayer.Dispose();
        }
        catch { }
    }
}
