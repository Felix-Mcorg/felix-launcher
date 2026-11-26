using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Felix.Core.Server;

public sealed record ServerStatus(
    bool Online,
    string Motd,
    int PlayersOnline,
    int PlayersMax,
    string VersionName,
    IReadOnlyList<string> PlayerNames,
    byte[]? Favicon)
{
    public static ServerStatus Offline { get; } =
        new(false, "", 0, 0, "", Array.Empty<string>(), null);
}

/// <summary>État d'un serveur Minecraft (protocole « Server List Ping »), sans bibliothèque externe.</summary>
public static class ServerPing
{
    public const int DefaultPort = 25565;

    public static (string Host, int Port) ParseAddress(string address)
    {
        var text = address.Trim();
        var colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text[(colon + 1)..], out var port) && port is > 0 and < 65536)
            return (text[..colon], port);
        return (text, DefaultPort);
    }

    /// <summary>Interroge le serveur. Ne lève pas d'erreur : un serveur injoignable donne ServerStatus.Offline.</summary>
    public static async Task<ServerStatus> QueryAsync(string address, TimeSpan timeout, CancellationToken ct = default)
    {
        var (host, port) = ParseAddress(address);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cts.Token);
            var stream = client.GetStream();

            var hostBytes = Encoding.UTF8.GetBytes(host);
            using var handshake = new MemoryStream();
            handshake.WriteByte(0x00);                               // identifiant du paquet : handshake
            handshake.Write(EncodeVarInt(763));                      // version du protocole (sans importance pour le statut)
            handshake.Write(EncodeVarInt(hostBytes.Length));
            handshake.Write(hostBytes);
            handshake.WriteByte((byte)(port >> 8));
            handshake.WriteByte((byte)(port & 0xFF));
            handshake.Write(EncodeVarInt(1));                        // état suivant : statut
            await WritePacketAsync(stream, handshake.ToArray(), cts.Token);
            await WritePacketAsync(stream, new byte[] { 0x00 }, cts.Token); // demande de statut

            await ReadVarIntAsync(stream, cts.Token);                // taille du paquet
            await ReadVarIntAsync(stream, cts.Token);                // identifiant du paquet
            var length = await ReadVarIntAsync(stream, cts.Token);   // taille du texte JSON
            if (length is <= 0 or > 1_000_000)
                return ServerStatus.Offline;

            var buffer = new byte[length];
            await stream.ReadExactlyAsync(buffer, cts.Token);
            return ParseResponse(Encoding.UTF8.GetString(buffer));
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException
                                       or JsonException or InvalidDataException or ObjectDisposedException)
        {
            return ServerStatus.Offline;
        }
    }

    public static ServerStatus ParseResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var motd = root.TryGetProperty("description", out var description) ? CleanMotd(description) : "";

        int online = 0, max = 0;
        var names = new List<string>();
        if (root.TryGetProperty("players", out var players))
        {
            if (players.TryGetProperty("online", out var o) && o.TryGetInt32(out var onlineValue))
                online = onlineValue;
            if (players.TryGetProperty("max", out var m) && m.TryGetInt32(out var maxValue))
                max = maxValue;
            if (players.TryGetProperty("sample", out var sample) && sample.ValueKind == JsonValueKind.Array)
            {
                foreach (var player in sample.EnumerateArray())
                {
                    if (player.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name)
                        names.Add(name);
                }
            }
        }

        var version = root.TryGetProperty("version", out var v) && v.TryGetProperty("name", out var vn)
            ? vn.GetString() ?? ""
            : "";

        byte[]? favicon = null;
        if (root.TryGetProperty("favicon", out var f) && f.GetString() is { } uri && uri.Contains(','))
        {
            try
            {
                favicon = Convert.FromBase64String(uri[(uri.IndexOf(',') + 1)..]);
            }
            catch (FormatException)
            {
                // image illisible : on l'ignore
            }
        }

        return new ServerStatus(true, motd, online, max, version, names, favicon);
    }

    public static string CleanMotd(JsonElement element) => Regex.Replace(Flatten(element), "§.", "").Trim();

    private static string Flatten(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.Array => string.Concat(element.EnumerateArray().Select(Flatten)),
        JsonValueKind.Object =>
            (element.TryGetProperty("text", out var text) ? Flatten(text) : "")
            + (element.TryGetProperty("extra", out var extra) ? Flatten(extra) : ""),
        _ => "",
    };

    public static byte[] EncodeVarInt(int value)
    {
        var bytes = new List<byte>();
        var v = (uint)value;
        do
        {
            var b = (byte)(v & 0x7F);
            v >>= 7;
            if (v != 0)
                b |= 0x80;
            bytes.Add(b);
        } while (v != 0);
        return bytes.ToArray();
    }

    private static async Task WritePacketAsync(Stream stream, byte[] data, CancellationToken ct)
    {
        await stream.WriteAsync(EncodeVarInt(data.Length), ct);
        await stream.WriteAsync(data, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<int> ReadVarIntAsync(Stream stream, CancellationToken ct)
    {
        var result = 0;
        var buffer = new byte[1];
        for (var i = 0; i < 5; i++)
        {
            await stream.ReadExactlyAsync(buffer, ct);
            result |= (buffer[0] & 0x7F) << (7 * i);
            if ((buffer[0] & 0x80) == 0)
                return result;
        }
        throw new InvalidDataException("VarInt trop long.");
    }
}
