namespace Halloween.Engine;

public sealed class BuiltinBot(string kind, int seed) : IGameBot
{
    private readonly Random random = new(seed);
    public static readonly BotIdentity[] Catalog =
    [
        new("hunter", "Jack le chasseur", "builtin"),
        new("wanderer", "Willow l'exploratrice", "builtin"),
        new("survivor", "Salem le prudent", "builtin")
    ];
    public BotIdentity Identity { get; } = Catalog.Single(b => b.Id == kind);

    public ValueTask<BotDecision> DecideAsync(Observation observation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var origin = observation.Player.Position;
        var walls = observation.Board.Walls.ToHashSet();
        (string Name, int X, int Y)[] directions = [("up", 0, -1), ("down", 0, 1), ("left", -1, 0), ("right", 1, 0)];
        if (observation.Player.Fire && kind != "wanderer")
            foreach (var direction in directions)
            {
                var p = origin.Offset(direction.X, direction.Y);
                while (observation.Player.Area.Contains(p) && !walls.Contains(p))
                {
                    if (observation.Enemies.Any(e => e.X == p.X && e.Y == p.Y) || observation.Players.Contains(p))
                        return ValueTask.FromResult(new BotDecision($"fire-{direction.Name}"));
                    p = p.Offset(direction.X, direction.Y);
                }
            }
        var available = directions.Where(d =>
        {
            var p = origin.Offset(d.X, d.Y);
            return p.X >= 0 && p.Y >= 0 && p.X < observation.Board.Size.Width && p.Y < observation.Board.Size.Height
                && !walls.Contains(p) && !observation.Players.Contains(p)
                && !observation.Enemies.Any(e => !e.Neutral && e.X == p.X && e.Y == p.Y);
        }).ToArray();
        if (available.Length == 0) return ValueTask.FromResult(new BotDecision(null));
        var scored = available.Select(d =>
        {
            var p = origin.Offset(d.X, d.Y);
            var danger = observation.Enemies.Where(e => !e.Neutral)
                .Select(e => Math.Abs(e.X - p.X) + Math.Abs(e.Y - p.Y)).DefaultIfEmpty(100).Min();
            var score = random.NextDouble() + (p != observation.Player.Previous ? 2 : 0)
                + (kind == "survivor" ? Math.Min(danger, 5) : 0)
                + (observation.Enemies.Any(e => e.Neutral && e.X == p.X && e.Y == p.Y) ? 8 : 0);
            return (d.Name, Score: score);
        });
        return ValueTask.FromResult(new BotDecision(scored.MaxBy(d => d.Score).Name));
    }
}
