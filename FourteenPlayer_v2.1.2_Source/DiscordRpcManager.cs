using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FourteenPlayer;

public class DiscordRpcManager : IDisposable
{
    private string _clientId;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _stateLock = new();

    private NamedPipeClientStream? _pipe;
    private bool _isConnected;
    private bool _isDisposed;

    private string _currentDetails = "Слушает FourteenPlayer";
    private string _currentState = "пока что тихо..";
    private string _largeImage = "https://github.com/muzdilapills/FourteenPlayer/raw/main/app_icon.png";
    private string _largeText = "FourteenPlayer";
    private long? _startTimestamp;
    private long? _endTimestamp;

    private const string DownloadUrl = "https://github.com/muzdilapills/FourteenPlayer/releases";
    private const string DownloadLabel = "Скачать FourteenPlayer";

    public bool IsEnabled { get; set; } = true;

    public DiscordRpcManager(string clientId)
    {
        _clientId = string.IsNullOrWhiteSpace(clientId) ? "383226320970055681" : clientId;
        Task.Run(WorkerLoopAsync);
    }

    public void UpdateClientId(string newClientId)
    {
        if (string.IsNullOrWhiteSpace(newClientId)) return;
        lock (_stateLock)
        {
            _clientId = newClientId.Trim();
        }
        lock (_sendLock)
        {
            Clear();
            try { _pipe?.Dispose(); } catch { }
            _pipe = null;
            _isConnected = false;
        }
    }

    public void UpdatePlaying(string trackTitle, string artist, TimeSpan position, TimeSpan duration)
    {
        if (_isDisposed || !IsEnabled) return;

        lock (_stateLock)
        {
            _currentDetails = string.IsNullOrWhiteSpace(trackTitle) ? "FourteenPlayer" : trackTitle.Trim();
            _currentState = string.IsNullOrWhiteSpace(artist) || artist == "FourteenPlayer" || artist == "Неизвестный исполнитель"
                ? "FourteenPlayer"
                : artist.Trim();
            _largeImage = "https://github.com/muzdilapills/FourteenPlayer/raw/main/app_icon.png";
            _largeText = "FourteenPlayer";

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long posSec = Math.Max(0, (long)position.TotalSeconds);
            long durSec = (long)duration.TotalSeconds;

            if (durSec > 0)
            {
                _startTimestamp = now - posSec;
                _endTimestamp = _startTimestamp + durSec;
            }
            else
            {
                _startTimestamp = now - posSec;
                _endTimestamp = null;
            }
        }

        SendCurrentActivity();
    }

    public void UpdatePaused(string? trackTitle = null)
    {
        if (_isDisposed || !IsEnabled) return;

        lock (_stateLock)
        {
            _currentDetails = "Пока что подождем....";
            _currentState = "";
            _largeImage = "https://github.com/muzdilapills/FourteenPlayer/raw/main/app_icon.png";
            _largeText = "FourteenPlayer (На паузе)";
            _startTimestamp = null;
            _endTimestamp = null;
        }

        SendCurrentActivity();
    }

    public void UpdateIdle()
    {
        if (_isDisposed || !IsEnabled) return;

        lock (_stateLock)
        {
            _currentDetails = "FourteenPlayer";
            _currentState = "пока что тихо..";
            _largeImage = "https://github.com/muzdilapills/FourteenPlayer/raw/main/app_icon.png";
            _largeText = "FourteenPlayer";
            _startTimestamp = null;
            _endTimestamp = null;
        }

        SendCurrentActivity();
    }

    public void Clear()
    {
        if (_isDisposed) return;
        try
        {
            if (_isConnected && _pipe != null && _pipe.IsConnected)
            {
                string clearJson = $@"{{
                    ""cmd"": ""SET_ACTIVITY"",
                    ""args"": {{
                        ""pid"": {Environment.ProcessId},
                        ""activity"": null
                    }},
                    ""nonce"": ""{Guid.NewGuid():N}""
                }}";
                SendRaw(1, clearJson);
            }
        }
        catch { }
    }

    private void SendCurrentActivity()
    {
        if (!_isConnected || _pipe == null || !_pipe.IsConnected) return;

        try
        {
            string details;
            string state;
            string largeImage;
            string largeText;
            long? start;
            long? end;

            lock (_stateLock)
            {
                details = _currentDetails;
                state = _currentState;
                largeImage = _largeImage;
                largeText = _largeText;
                start = _startTimestamp;
                end = _endTimestamp;
            }

            var stateJson = !string.IsNullOrWhiteSpace(state)
                ? $@", ""state"": {JsonSerializer.Serialize(state)}"
                : "";

            var timestampsObj = (start.HasValue || end.HasValue)
                ? (end.HasValue
                    ? $@", ""timestamps"": {{ ""start"": {start}, ""end"": {end} }}"
                    : $@", ""timestamps"": {{ ""start"": {start} }}")
                : "";

            string payload = $@"{{
                ""cmd"": ""SET_ACTIVITY"",
                ""args"": {{
                    ""pid"": {Environment.ProcessId},
                    ""activity"": {{
                        ""type"": 2,
                        ""name"": ""FourteenPlayer"",
                        ""details"": {JsonSerializer.Serialize(details)}{stateJson}{timestampsObj},
                        ""assets"": {{
                            ""large_image"": {JsonSerializer.Serialize(largeImage)},
                            ""large_text"": {JsonSerializer.Serialize(largeText)}
                        }},
                        ""buttons"": [
                            {{
                                ""label"": {JsonSerializer.Serialize(DownloadLabel)},
                                ""url"": {JsonSerializer.Serialize(DownloadUrl)}
                            }}
                        ]
                    }}
                }},
                ""nonce"": ""{Guid.NewGuid():N}""
            }}";

            SendRaw(1, payload);
        }
        catch
        {
            _isConnected = false;
        }
    }

    private readonly object _sendLock = new();
    private void SendRaw(int opcode, string json)
    {
        lock (_sendLock)
        {
            if (_pipe == null || !_pipe.IsConnected) return;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                using var writer = new BinaryWriter(_pipe, Encoding.UTF8, leaveOpen: true);
                writer.Write(opcode);
                writer.Write(bytes.Length);
                writer.Write(bytes);
                writer.Flush();
            }
            catch
            {
                _isConnected = false;
            }
        }
    }

    private async Task WorkerLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            if (!_isConnected)
            {
                TryConnectToDiscord();
            }

            if (_isConnected && _pipe != null && _pipe.IsConnected)
            {
                SendCurrentActivity();

                // Read incoming events / heartbeats to detect pipe closure
                try
                {
                    byte[] buffer = new byte[4096];
                    while (!_cts.IsCancellationRequested && _pipe.IsConnected)
                    {
                        int read = await _pipe.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                        if (read == 0)
                        {
                            _isConnected = false;
                            break;
                        }
                    }
                }
                catch
                {
                    _isConnected = false;
                }
            }

            // Sleep before reconnection attempt
            try
            {
                await Task.Delay(3000, _cts.Token);
            }
            catch { break; }
        }
    }

    private void TryConnectToDiscord()
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                string pipeName = $"discord-ipc-{i}";
                var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                client.Connect(250);

                // Handshake
                string currentId;
                lock (_stateLock) { currentId = _clientId; }
                string handshake = $"{{\"v\": 1, \"client_id\": \"{currentId}\"}}";
                byte[] bytes = Encoding.UTF8.GetBytes(handshake);

                using (var writer = new BinaryWriter(client, Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(0);
                    writer.Write(bytes.Length);
                    writer.Write(bytes);
                    writer.Flush();
                }

                // Read Handshake Response
                using (var reader = new BinaryReader(client, Encoding.UTF8, leaveOpen: true))
                {
                    int opcode = reader.ReadInt32();
                    int length = reader.ReadInt32();
                    byte[] resp = reader.ReadBytes(length);
                    string respStr = Encoding.UTF8.GetString(resp);

                    if (opcode == 1 && respStr.Contains("\"READY\""))
                    {
                        _pipe = client;
                        _isConnected = true;
                        return;
                    }
                }

                client.Dispose();
            }
            catch
            {
                // Next pipe
            }
        }
        _isConnected = false;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            Clear();
            _cts.Cancel();
            _pipe?.Dispose();
        }
        catch { }
    }
}
