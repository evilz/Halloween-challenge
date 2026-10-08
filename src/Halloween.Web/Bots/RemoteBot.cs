using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Halloween.Engine;

namespace Halloween.Web.Bots;

public sealed class RemoteBot(BotIdentity identity, HttpClient client) : IGameBot
{
    public BotIdentity Identity { get; } = identity;
    public async ValueTask<BotDecision> DecideAsync(Observation observation, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(new Uri(new Uri(Identity.Url!), "move"), observation, cancellationToken);
        response.EnsureSuccessStatusCode();
        var move = await BotHttp.ReadJsonAsync<MoveResponse>(response, cancellationToken);
        if (move.Move is null) return new(null);
        return Commands.Moves.Contains(move.Move, StringComparer.Ordinal)
            ? new(move.Move) : new(null, "commande inconnue");
    }
}

public static class BotHttp
{
    public static Uri ParseBaseUrl(string url)
    {
        if (url.Length > 300 || !Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Indiquez une URL HTTP(S) sans identifiants, paramètres ni fragment.");
        return uri;
    }

    public static SocketsHttpHandler CreateHandler(bool allowLocalBots) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        MaxConnectionsPerServer = 12,
        ConnectTimeout = TimeSpan.FromSeconds(3),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        // Validate the actual IP used by the socket, including after DNS changes.
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            var allowed = addresses.Where(a => IsPublicAddress(a) || (allowLocalBots && IPAddress.IsLoopback(a))).ToArray();
            if (allowed.Length == 0) throw new HttpRequestException("Adresse privée ou réservée interdite pour un bot.");
            foreach (var address in allowed)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (SocketException) { socket.Dispose(); }
                catch { socket.Dispose(); throw; }
            }
            throw new HttpRequestException("Impossible de joindre le bot.");
        }
    };

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            // Only global unicast, excluding documentation and special-purpose ranges.
            return (b[0] & 0xe0) == 0x20 && !(b[0] == 0x20 && b[1] == 0x01 && b[2] < 2)
                && !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8)
                && !(b[0] == 0x20 && b[1] == 0x02);
        return address.AddressFamily == AddressFamily.InterNetwork && b[0] is > 0 and < 224
            && b[0] is not (10 or 127) && !(b[0] == 100 && b[1] is >= 64 and <= 127)
            && !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] is >= 16 and <= 31)
            && !(b[0] == 192 && (b[1] == 168 || b[1] == 0 || (b[1] == 88 && b[2] == 99)))
            && !(b[0] == 198 && b[1] is 18 or 19)
            && !(b[0] == 198 && b[1] == 51 && b[2] == 100)
            && !(b[0] == 203 && b[1] == 0 && b[2] == 113);
    }

    public static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // HttpClient also caps its buffered response. Reject oversized payloads here for clearer validation.
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length > 16_384) throw new ArgumentException("La réponse du bot dépasse 16 Ko.");
        return JsonSerializer.Deserialize<T>(bytes, JsonSerializerOptions.Web) ?? throw new JsonException("Réponse vide.");
    }
}
