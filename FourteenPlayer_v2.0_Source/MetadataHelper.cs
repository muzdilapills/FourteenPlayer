using System;
using System.IO;
using System.Text;

namespace FourteenPlayer;

public static class MetadataHelper
{
    private static readonly string[] SupportedExtensions = { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".wma", ".ogg" };

    public static bool IsSupportedAudioFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return false;
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return Array.Exists(SupportedExtensions, e => e == ext);
    }

    public static (string title, string artist, string album) ReadBasicTags(string filePath)
    {
        string fallbackTitle = Path.GetFileNameWithoutExtension(filePath);
        string fallbackArtist = "Неизвестный исполнитель";
        string fallbackAlbum = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "Неизвестный альбом";

        try
        {
            if (!File.Exists(filePath))
                return (fallbackTitle, fallbackArtist, fallbackAlbum);

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            
            // Try ID3v2 at start of MP3
            if (fs.Length > 10)
            {
                byte[] header = new byte[10];
                fs.ReadExactly(header, 0, 10);
                if (header[0] == 'I' && header[1] == 'D' && header[2] == '3')
                {
                    int tagSize = ((header[6] & 0x7F) << 21) |
                                  ((header[7] & 0x7F) << 14) |
                                  ((header[8] & 0x7F) << 7) |
                                  (header[9] & 0x7F);

                    if (tagSize > 0 && tagSize < 1000000 && (fs.Position + tagSize) <= fs.Length)
                    {
                        byte[] tagData = new byte[tagSize];
                        fs.ReadExactly(tagData, 0, tagSize);
                        
                        string title = ExtractId3v2Frame(tagData, "TIT2");
                        string artist = ExtractId3v2Frame(tagData, "TPE1");
                        string album = ExtractId3v2Frame(tagData, "TALB");

                        if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(artist))
                        {
                            return (
                                string.IsNullOrWhiteSpace(title) ? fallbackTitle : title.Trim(),
                                string.IsNullOrWhiteSpace(artist) ? fallbackArtist : artist.Trim(),
                                string.IsNullOrWhiteSpace(album) ? fallbackAlbum : album.Trim()
                            );
                        }
                    }
                }
            }

            // Try ID3v1 at end of MP3 (128 bytes)
            if (fs.Length > 128)
            {
                fs.Seek(-128, SeekOrigin.End);
                byte[] id3v1 = new byte[128];
                fs.ReadExactly(id3v1, 0, 128);

                if (id3v1[0] == 'T' && id3v1[1] == 'A' && id3v1[2] == 'G')
                {
                    string title = Encoding.Default.GetString(id3v1, 3, 30).Trim('\0', ' ');
                    string artist = Encoding.Default.GetString(id3v1, 33, 30).Trim('\0', ' ');
                    string album = Encoding.Default.GetString(id3v1, 63, 30).Trim('\0', ' ');

                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        return (
                            title,
                            string.IsNullOrWhiteSpace(artist) ? fallbackArtist : artist,
                            string.IsNullOrWhiteSpace(album) ? fallbackAlbum : album
                        );
                    }
                }
            }
        }
        catch
        {
            // Ignore parsing errors, return filename
        }

        // Try splitting "Artist - Title.mp3" from filename
        if (fallbackTitle.Contains(" - "))
        {
            var parts = fallbackTitle.Split(new[] { " - " }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                return (parts[1].Trim(), parts[0].Trim(), fallbackAlbum);
            }
        }

        return (fallbackTitle, fallbackArtist, fallbackAlbum);
    }

    private static string ExtractId3v2Frame(byte[] data, string frameId)
    {
        byte[] target = Encoding.ASCII.GetBytes(frameId);
        for (int i = 0; i <= data.Length - 10; i++)
        {
            if (data[i] == target[0] && data[i + 1] == target[1] && data[i + 2] == target[2] && data[i + 3] == target[3])
            {
                int frameSize = (data[i + 4] << 24) | (data[i + 5] << 16) | (data[i + 6] << 8) | data[i + 7];
                // In some versions syncsafe integer
                if (frameSize > 0 && frameSize <= 500 && (i + 10 + frameSize) <= data.Length)
                {
                    int encodingByte = data[i + 10];
                    int contentOffset = i + 11;
                    int contentLength = frameSize - 1;
                    if (contentLength > 0)
                    {
                        try
                        {
                            return encodingByte switch
                            {
                                1 => Encoding.Unicode.GetString(data, contentOffset, contentLength).Trim('\0', ' '),
                                2 => Encoding.BigEndianUnicode.GetString(data, contentOffset, contentLength).Trim('\0', ' '),
                                3 => Encoding.UTF8.GetString(data, contentOffset, contentLength).Trim('\0', ' '),
                                _ => Encoding.Default.GetString(data, contentOffset, contentLength).Trim('\0', ' ')
                            };
                        }
                        catch { }
                    }
                }
            }
        }
        return "";
    }
}
