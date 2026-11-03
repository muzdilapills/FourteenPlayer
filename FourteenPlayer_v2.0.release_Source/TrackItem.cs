using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace FourteenPlayer;

public class TrackItem : INotifyPropertyChanged
{
    private bool _isPlaying;
    private int _trackNumber;
    private string _title = "";
    private string _artist = "";
    private string _album = "";
    private string _durationString = "--:--";
    private TimeSpan _duration = TimeSpan.Zero;

    public string FilePath { get; set; } = "";

    public int TrackNumber
    {
        get => _trackNumber;
        set { _trackNumber = value; OnPropertyChanged(); }
    }

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public string Artist
    {
        get => _artist;
        set { _artist = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplaySubtitle)); }
    }

    public string Album
    {
        get => _album;
        set { _album = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplaySubtitle)); }
    }

    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            _duration = value;
            DurationString = value.TotalHours >= 1
                ? value.ToString(@"h\:mm\:ss")
                : value.ToString(@"m\:ss");
            OnPropertyChanged();
        }
    }

    public string DurationString
    {
        get => _durationString;
        set { _durationString = value; OnPropertyChanged(); }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set { _isPlaying = value; OnPropertyChanged(); }
    }

    public string DisplaySubtitle
    {
        get
        {
            bool hasArtist = !string.IsNullOrWhiteSpace(Artist) && !Artist.Equals("Неизвестный исполнитель", StringComparison.OrdinalIgnoreCase);
            bool hasAlbum = !string.IsNullOrWhiteSpace(Album) && !Album.Equals("Неизвестный альбом", StringComparison.OrdinalIgnoreCase) && !Album.Equals(Artist, StringComparison.OrdinalIgnoreCase);

            if (hasArtist && hasAlbum)
                return $"{Artist} • {Album}";
            if (hasArtist)
                return Artist;
            if (hasAlbum)
                return Album;

            return Path.GetFileName(FilePath);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
