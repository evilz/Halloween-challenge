using Halloween.Engine;

namespace Halloween.Web;

public sealed class MatchSession(GameEngine engine)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public GameSnapshot State { get; private set; } = engine.Snapshot();
    public async Task<GameSnapshot> StepAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { State = await engine.StepAsync(cancellationToken); return State; }
        finally { gate.Release(); }
    }
}

public sealed class MatchStore
{
    private readonly Dictionary<Guid, MatchSession> matches = [];
    private readonly object gate = new();
    public MatchSession Create(GameOptions options, IGameBot[] bots)
    {
        lock (gate)
        {
            if (matches.Count >= 32) throw new ArgumentException("32 parties sont déjà conservées. Supprimez une partie ou redémarrez le serveur.");
            var session = new MatchSession(new GameEngine(options, bots));
            matches.Add(session.State.Id, session);
            return session;
        }
    }
    public MatchSession? Find(Guid id) { lock (gate) return matches.GetValueOrDefault(id); }
    public bool Remove(Guid id) { lock (gate) return matches.Remove(id); }
}
public sealed record CreateMatchRequest(GameOptions? Options, string[]? BotIds);
public sealed record RegisterBotRequest(string? Url);
