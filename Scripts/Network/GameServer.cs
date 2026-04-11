using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace SpacedOut.Network;

public class ClientConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public TcpClient TcpClient { get; set; } = null!;
    public NetworkStream Stream { get; set; } = null!;
    public State.StationRole? Role { get; set; }
    public bool IsConnected { get; set; } = true;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
}

public partial class GameServer : Node
{
    [Signal] public delegate void ClientConnectedEventHandler(string clientId);
    [Signal] public delegate void ClientDisconnectedEventHandler(string clientId);
    [Signal] public delegate void CommandReceivedEventHandler(string clientId, string messageJson);
    [Signal] public delegate void RoleSelectedEventHandler(string clientId, string role);

    private TcpListener? _listener;
    private readonly ConcurrentDictionary<string, ClientConnection> _clients = new();
    private readonly ConcurrentQueue<(string clientId, string message)> _incomingMessages = new();
    private CancellationTokenSource? _cts;
    private string _webRoot = "";
    private int _port = 8080;

    public IReadOnlyDictionary<string, ClientConnection> Clients => _clients;

    public override void _Ready()
    {
        _webRoot = ProjectSettings.GlobalizePath("res://WebClients");
        GD.Print($"[GameServer] Web root: {_webRoot}");
    }

    public void StartServer(int port = 8080)
    {
        _port = port;
        _cts = new CancellationTokenSource();

        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            GD.Print($"[GameServer] Listening on port {_port}");
            PrintLocalAddresses();
            _ = AcceptClientsAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameServer] Failed to start: {ex.Message}");
        }
    }

    private void PrintLocalAddresses()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    GD.Print($"[GameServer] Access at: http://{ip}:{_port}");
            }
        }
        catch { }
    }

    public void StopServer()
    {
        _cts?.Cancel();
        _listener?.Stop();
        foreach (var client in _clients.Values)
        {
            try { client.TcpClient.Close(); } catch { }
        }
        _clients.Clear();
        GD.Print("[GameServer] Stopped");
    }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var tcpClient = await _listener!.AcceptTcpClientAsync(ct);
                _ = HandleConnectionAsync(tcpClient, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { GD.PrintErr($"[GameServer] Accept error: {ex.Message}"); }
        }
    }

    private async Task HandleConnectionAsync(TcpClient tcpClient, CancellationToken ct)
    {
        NetworkStream stream;
        try
        {
            stream = tcpClient.GetStream();
            stream.ReadTimeout = 5000;
        }
        catch { tcpClient.Close(); return; }

        try
        {
            string request = await ReadHttpHeadersAsync(stream, ct);
            if (request.Length == 0) { tcpClient.Close(); return; }

            var headers = ParseHttpHeaders(request);

            if (headers.TryGetValue("Upgrade", out var upgrade) &&
                upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase))
            {
                stream.ReadTimeout = Timeout.Infinite;
                await HandleWebSocketUpgrade(tcpClient, stream, headers, ct);
            }
            else
            {
                await HandleHttpRequest(tcpClient, stream, request);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameServer] Connection error: {ex.Message}");
            try { tcpClient.Close(); } catch { }
        }
    }

    private static async Task<string> ReadHttpHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[4096];
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int bytesRead = await stream.ReadAsync(buffer, totalRead, buffer.Length - totalRead, ct);
            if (bytesRead == 0) break;
            totalRead += bytesRead;

            for (int i = 3; i < totalRead; i++)
            {
                if (buffer[i - 3] == '\r' && buffer[i - 2] == '\n' &&
                    buffer[i - 1] == '\r' && buffer[i] == '\n')
                {
                    return Encoding.UTF8.GetString(buffer, 0, totalRead);
                }
            }
        }

        return totalRead > 0 ? Encoding.UTF8.GetString(buffer, 0, totalRead) : "";
    }

    private static Dictionary<string, string> ParseHttpHeaders(string request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = request.Split("\r\n");

        if (lines.Length > 0)
        {
            var parts = lines[0].Split(' ');
            if (parts.Length >= 2)
            {
                headers["Method"] = parts[0];
                headers["Path"] = parts[1];
            }
        }

        for (int i = 1; i < lines.Length; i++)
        {
            int colonIdx = lines[i].IndexOf(':');
            if (colonIdx > 0)
            {
                string key = lines[i][..colonIdx].Trim();
                string value = lines[i][(colonIdx + 1)..].Trim();
                headers[key] = value;
            }
        }

        return headers;
    }

    #region HTTP Static File Server

    private async Task HandleHttpRequest(TcpClient tcpClient, NetworkStream stream, string request)
    {
        try
        {
            var lines = request.Split("\r\n");
            if (lines.Length == 0) { tcpClient.Close(); return; }

            var parts = lines[0].Split(' ');
            if (parts.Length < 2) { tcpClient.Close(); return; }

            string path = Uri.UnescapeDataString(parts[1].Split('?')[0]);
            if (path == "/") path = "/index.html";

            string filePath = Path.Combine(_webRoot, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            filePath = Path.GetFullPath(filePath);

            if (!filePath.StartsWith(Path.GetFullPath(_webRoot)))
            {
                await SendHttpResponse(stream, 403, "Forbidden", "text/plain", "Forbidden"u8.ToArray());
                tcpClient.Close();
                return;
            }

            if (File.Exists(filePath))
            {
                byte[] content = await File.ReadAllBytesAsync(filePath);
                string contentType = GetContentType(filePath);
                await SendHttpResponse(stream, 200, "OK", contentType, content);
            }
            else
            {
                await SendHttpResponse(stream, 404, "Not Found", "text/plain", "Not Found"u8.ToArray());
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameServer] HTTP error: {ex.Message}");
        }
        finally
        {
            try { tcpClient.Close(); } catch { }
        }
    }

    private static async Task SendHttpResponse(NetworkStream stream, int statusCode, string statusText,
        string contentType, byte[] body)
    {
        string header = $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                        $"Content-Type: {contentType}\r\n" +
                        $"Content-Length: {body.Length}\r\n" +
                        "Access-Control-Allow-Origin: *\r\n" +
                        "Connection: close\r\n\r\n";
        byte[] headerBytes = Encoding.UTF8.GetBytes(header);
        await stream.WriteAsync(headerBytes);
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private static string GetContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "application/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        _ => "application/octet-stream"
    };

    #endregion

    #region WebSocket Server

    private async Task HandleWebSocketUpgrade(TcpClient tcpClient, NetworkStream stream,
        Dictionary<string, string> headers, CancellationToken ct)
    {
        if (!headers.TryGetValue("Sec-WebSocket-Key", out var wsKey))
        {
            tcpClient.Close();
            return;
        }

        string acceptKey = ComputeWebSocketAccept(wsKey);
        string response = "HTTP/1.1 101 Switching Protocols\r\n" +
                          "Upgrade: websocket\r\n" +
                          "Connection: Upgrade\r\n" +
                          $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";

        await stream.WriteAsync(Encoding.UTF8.GetBytes(response), ct);

        var client = new ClientConnection
        {
            TcpClient = tcpClient,
            Stream = stream,
        };
        _clients[client.Id] = client;

        CallDeferred("emit_signal", SignalName.ClientConnected, client.Id);
        GD.Print($"[GameServer] WebSocket client connected: {client.Id}");

        _ = ReadWebSocketFramesAsync(client, ct);
    }

    private static string ComputeWebSocketAccept(string key)
    {
        string combined = key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToBase64String(hash);
    }

    private async Task ReadWebSocketFramesAsync(ClientConnection client, CancellationToken ct)
    {
        var stream = client.Stream;
        var singleByte = new byte[1];
        try
        {
            while (!ct.IsCancellationRequested && client.IsConnected)
            {
                int read = await stream.ReadAsync(singleByte, 0, 1, ct);
                if (read == 0) break;
                int firstByte = singleByte[0];

                int opcode = firstByte & 0x0F;
                read = await stream.ReadAsync(singleByte, 0, 1, ct);
                if (read == 0) break;
                int secondByte = singleByte[0];

                bool isMasked = (secondByte & 0x80) != 0;
                long payloadLength = secondByte & 0x7F;

                if (payloadLength == 126)
                {
                    byte[] lenBytes = new byte[2];
                    await stream.ReadExactlyAsync(lenBytes, ct);
                    payloadLength = (lenBytes[0] << 8) | lenBytes[1];
                }
                else if (payloadLength == 127)
                {
                    byte[] lenBytes = new byte[8];
                    await stream.ReadExactlyAsync(lenBytes, ct);
                    payloadLength = 0;
                    for (int i = 0; i < 8; i++)
                        payloadLength = (payloadLength << 8) | lenBytes[i];
                }

                byte[] maskKey = Array.Empty<byte>();
                if (isMasked)
                {
                    maskKey = new byte[4];
                    await stream.ReadExactlyAsync(maskKey, ct);
                }

                byte[] payload = new byte[payloadLength];
                if (payloadLength > 0)
                    await stream.ReadExactlyAsync(payload, ct);

                if (isMasked)
                {
                    for (int i = 0; i < payload.Length; i++)
                        payload[i] ^= maskKey[i % 4];
                }

                switch (opcode)
                {
                    case 0x1: // Text
                        string text = Encoding.UTF8.GetString(payload);
                        _incomingMessages.Enqueue((client.Id, text));
                        client.LastActivity = DateTime.UtcNow;
                        break;
                    case 0x8: // Close
                        await SendWebSocketClose(client);
                        DisconnectClient(client.Id);
                        return;
                    case 0x9: // Ping
                        await SendWebSocketFrame(client, 0xA, payload);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                GD.PrintErr($"[GameServer] WebSocket read error ({client.Id}): {ex.Message}");
        }
        finally
        {
            DisconnectClient(client.Id);
        }
    }

    private async Task SendWebSocketFrame(ClientConnection client, int opcode, byte[] payload)
    {
        try
        {
            var stream = client.Stream;
            byte[] frame;

            if (payload.Length < 126)
            {
                frame = new byte[2 + payload.Length];
                frame[0] = (byte)(0x80 | opcode);
                frame[1] = (byte)payload.Length;
                Buffer.BlockCopy(payload, 0, frame, 2, payload.Length);
            }
            else if (payload.Length <= 65535)
            {
                frame = new byte[4 + payload.Length];
                frame[0] = (byte)(0x80 | opcode);
                frame[1] = 126;
                frame[2] = (byte)(payload.Length >> 8);
                frame[3] = (byte)(payload.Length & 0xFF);
                Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);
            }
            else
            {
                frame = new byte[10 + payload.Length];
                frame[0] = (byte)(0x80 | opcode);
                frame[1] = 127;
                long len = payload.Length;
                for (int i = 7; i >= 0; i--)
                {
                    frame[2 + i] = (byte)(len & 0xFF);
                    len >>= 8;
                }
                Buffer.BlockCopy(payload, 0, frame, 10, payload.Length);
            }

            await stream.WriteAsync(frame);
            await stream.FlushAsync();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameServer] Send error ({client.Id}): {ex.Message}");
            DisconnectClient(client.Id);
        }
    }

    private async Task SendWebSocketClose(ClientConnection client)
    {
        try
        {
            await SendWebSocketFrame(client, 0x8, Array.Empty<byte>());
        }
        catch { }
    }

    public void SendToClient(string clientId, string message)
    {
        if (_clients.TryGetValue(clientId, out var client) && client.IsConnected)
        {
            byte[] payload = Encoding.UTF8.GetBytes(message);
            _ = SendWebSocketFrame(client, 0x1, payload);
        }
    }

    public void SendToRole(State.StationRole role, string message)
    {
        foreach (var client in _clients.Values)
        {
            if (client.Role == role && client.IsConnected)
                SendToClient(client.Id, message);
        }
    }

    public void BroadcastToAll(string message)
    {
        foreach (var client in _clients.Values)
        {
            if (client.IsConnected)
                SendToClient(client.Id, message);
        }
    }

    private void DisconnectClient(string clientId)
    {
        if (_clients.TryRemove(clientId, out var client))
        {
            client.IsConnected = false;
            try { client.TcpClient.Close(); } catch { }
            CallDeferred("emit_signal", SignalName.ClientDisconnected, clientId);
            GD.Print($"[GameServer] Client disconnected: {clientId} (Role: {client.Role})");
        }
    }

    #endregion

    public override void _Process(double delta)
    {
        while (_incomingMessages.TryDequeue(out var msg))
        {
            try
            {
                var json = JsonDocument.Parse(msg.message);
                var root = json.RootElement;

                if (root.TryGetProperty("type", out var typeElem))
                {
                    string type = typeElem.GetString() ?? "";

                    if (type == "select_role" && root.TryGetProperty("role", out var roleElem))
                    {
                        EmitSignal(SignalName.RoleSelected, msg.clientId, roleElem.GetString() ?? "");
                    }
                    else if (type == "command")
                    {
                        EmitSignal(SignalName.CommandReceived, msg.clientId, msg.message);
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[GameServer] Parse error from {msg.clientId}: {ex.Message}");
            }
        }
    }

    public void AssignRole(string clientId, State.StationRole role)
    {
        if (_clients.TryGetValue(clientId, out var client))
        {
            client.Role = role;
            var response = JsonSerializer.Serialize(new
            {
                type = "role_assigned",
                role = role.ToString(),
                client_id = clientId
            });
            SendToClient(clientId, response);
            GD.Print($"[GameServer] Assigned role {role} to {clientId}");
        }
    }

    public bool IsRoleTaken(State.StationRole role)
    {
        return _clients.Values.Any(c => c.Role == role && c.IsConnected);
    }

    public List<State.StationRole> GetAvailableRoles()
    {
        var taken = _clients.Values.Where(c => c.IsConnected && c.Role.HasValue)
            .Select(c => c.Role!.Value).ToHashSet();
        return Enum.GetValues<State.StationRole>()
            .Where(r => r != State.StationRole.Observer && !taken.Contains(r)).ToList();
    }

    public override void _ExitTree()
    {
        StopServer();
    }
}
