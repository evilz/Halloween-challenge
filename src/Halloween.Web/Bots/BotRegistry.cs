using System.Text.Json;
using Halloween.Engine;

namespace Halloween.Web.Bots;

public sealed class BotRegistry
{
    private readonly string path;
    private readonly List<BotIdentity> remote;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly IHttpClientFactory clients;
    public BotRegistry(IWebHostEnvironment environment, IHttpClientFactory clients)
    {
        this.clients = clients;
        path = Path.Combine(environment.ContentRootPath, "App_Data", "bots.json");
        remote = File.Exists(path) ? JsonSerializer.Deserialize<List<BotIdentity>>(File.ReadAllText(path), JsonSerializerOptions.Web) ?? [] : [];
    }
    public async Task<BotIdentity[]> ListAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try { return [.. BuiltinBot.Catalog, .. remote]; }
        finally { gate.Release(); }
    }
    public async Task<BotIdentity> RegisterAsync(string url, CancellationToken cancellationToken)
    {
        var uri = BotHttp.ParseBaseUrl(url);
        var client = clients.CreateClient("bots");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var response = await client.PostAsync(new Uri(uri, "name"), null, timeout.Token);
        response.EnsureSuccessStatusCode();
        var name = await BotHttp.ReadJsonAsync<NameResponse>(response, timeout.Token);
        if (string.IsNullOrWhiteSpace(name.Name) || name.Name.Length > 40)
            throw new ArgumentException("Le /name doit renvoyer un nom de 1 à 40 caractères.");
        var identity = new BotIdentity(Guid.NewGuid().ToString("N"), name.Name.Trim(), "remote", uri.AbsoluteUri);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (remote.FirstOrDefault(b => b.Url == identity.Url) is { } existing) return existing;
            if (remote.Count >= 100) throw new ArgumentException("Le registre a atteint sa limite de 100 bots.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var updated = remote.Append(identity).ToArray();
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(updated, JsonSerializerOptions.Web), cancellationToken);
            File.Move(temporary, path, overwrite: true);
            remote.Add(identity);
            return identity;
        }
        finally { gate.Release(); }
    }
    public async Task<IGameBot[]> ResolveAsync(string[] ids, int seed, CancellationToken cancellationToken)
    {
        if (ids.Length is < 1 or > 9 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException("Sélectionnez entre 1 et 9 bots différents.");
        var catalog = await ListAsync(cancellationToken);
        return ids.Select((id, index) =>
        {
            var bot = catalog.FirstOrDefault(b => b.Id == id) ?? throw new ArgumentException("Un bot sélectionné n'existe plus.");
            return bot.Kind == "builtin" ? (IGameBot)new BuiltinBot(bot.Id, unchecked(seed + index * 7919))
                : new RemoteBot(bot, clients.CreateClient("bots"));
        }).ToArray();
    }
}
