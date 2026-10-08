namespace Halloween.Engine;

public sealed class GameEngine
{
    private sealed class Player(IGameBot bot, Guid id, Position position, string color)
    {
        public IGameBot Bot { get; } = bot;
        public Guid Id { get; } = id;
        public Position Position { get; set; } = position;
        public Position Previous { get; set; } = position;
        public string Color { get; } = color;
        public bool Alive { get; set; } = true;
        public int NextFireTurn { get; set; }
        public int RespawnTurn { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int SurvivalTurns { get; set; }
        public int Score { get; set; }
        public string? LastMove { get; set; }
        public string? LastError { get; set; }
    }

    private sealed class Enemy(Guid id, Position position, int neutralRemaining)
    {
        public Guid Id { get; } = id;
        public Position Position { get; set; } = position;
        public int NeutralRemaining { get; set; } = neutralRemaining;
    }

    private readonly Random random;
    private readonly HashSet<Position> walls;
    private readonly List<Player> players = [];
    private readonly List<Enemy> enemies = [];
    private readonly List<GameEvent> events = [];
    private readonly List<ShotSnapshot> shots = [];
    private readonly SemaphoreSlim stepLock = new(1, 1);
    private static readonly string[] Colors = ["#ffb454", "#b4a1ff", "#64dfcb", "#ff7098", "#81c7ff", "#e9da79", "#d59bf6", "#a6d98a", "#f4ae93"];
    public Guid Id { get; } = Guid.NewGuid();
    public GameOptions Options { get; }
    public int Turn { get; private set; }
    public bool Finished => Turn >= Options.MaxTurns;

    public GameEngine(GameOptions options, IReadOnlyList<IGameBot> bots)
        : this(options, bots, null, null, null) { }

    // Deterministic scenario seam for rule tests; never exposed over HTTP.
    internal GameEngine(GameOptions options, IReadOnlyList<IGameBot> bots, HashSet<Position>? scenarioWalls,
        Position[]? playerPositions, (Position Position, int Neutral)[]? enemyPositions)
    {
        if (options.Validate() is { } error) throw new ArgumentException(error, nameof(options));
        if (bots.Count is < 1 or > 9) throw new ArgumentException("Une partie nécessite entre 1 et 9 bots.", nameof(bots));
        Options = options;
        random = new(options.Seed);
        walls = scenarioWalls ?? Maze.Generate(options.Width, options.Height, random);
        for (var i = 0; i < bots.Count; i++)
        {
            var position = playerPositions?[i] ?? FreeCell() ?? throw new ArgumentException("Pas assez de cases libres.");
            players.Add(new(bots[i], NextId(), position, Colors[i]));
        }
        if (enemyPositions is not null)
            enemies.AddRange(enemyPositions.Select(e => new Enemy(NextId(), e.Position, e.Neutral)));
        else
            for (var i = 0; i < options.InitialEnemies; i++) SpawnEnemy();
        AddEvent("start", $"{bots.Count} bots entrent dans le labyrinthe. Graine {options.Seed}.");
    }

    public Observation Observe(Guid playerId)
    {
        var player = players.Single(p => p.Id == playerId);
        var area = VisibleArea(player.Position);
        return new(new(Id), new(player.Id, player.Bot.Identity.Name, player.Position, player.Previous, area,
                player.Alive && Turn >= player.NextFireTurn),
            new(new(Options.Width, Options.Height), walls.Where(area.Contains).OrderBy(p => p.Y).ThenBy(p => p.X).ToArray()),
            players.Where(p => p.Alive && p.Id != playerId && area.Contains(p.Position)).Select(p => p.Position).ToArray(),
            enemies.Where(e => area.Contains(e.Position)).Select(e => new EnemyView(e.Position.X, e.Position.Y, e.NeutralRemaining > 0)).ToArray());
    }

    public async Task<GameSnapshot> StepAsync(CancellationToken cancellationToken = default)
    {
        await stepLock.WaitAsync(cancellationToken);
        try
        {
            if (Finished) return Snapshot();
            // Collect before mutating. Every bot sees exactly the same turn.
            var active = players.Where(p => p.Alive).ToArray();
            var decisions = await Task.WhenAll(active.Select(async p => (Player: p,
                Decision: await AskBotAsync(p.Bot, Observe(p.Id), cancellationToken))));
            cancellationToken.ThrowIfCancellationRequested();
            Turn++;
            shots.Clear();
            RespawnPlayers();
            foreach (var player in players) { player.Previous = player.Position; player.LastMove = null; player.LastError = null; }

            var destinations = players.Where(p => p.Alive).ToDictionary(p => p, p => p.Position);
            var firing = new List<(Player Player, int Dx, int Dy)>();
            foreach (var (player, decision) in decisions)
            {
                player.LastError = decision.Error;
                player.LastMove = decision.Move;
                if (decision.Error is { } botError) AddEvent("bot-error", $"{player.Bot.Identity.Name} : {botError}");
                if (!Commands.TryParse(decision.Move, out var dx, out var dy, out var fire)) continue;
                if (fire)
                {
                    if (Turn - 1 >= player.NextFireTurn) firing.Add((player, dx, dy));
                }
                else
                {
                    var target = player.Position.Offset(dx, dy);
                    if (Walkable(target)) destinations[player] = target;
                }
            }
            foreach (var (player, target) in destinations) player.Position = target;
            foreach (var collision in destinations.GroupBy(pair => pair.Value).Where(group => group.Count() > 1))
                foreach (var (player, _) in collision) Kill(player, "collision entre bots");
            foreach (var player in players.Where(p => p.Alive).ToArray()) ResolveContact(player);

            // Record all hits before applying them, allowing mutual kills.
            var playerHits = new Dictionary<Player, Player>();
            var enemyHits = new Dictionary<Enemy, Player>();
            foreach (var (shooter, dx, dy) in firing.Where(f => f.Player.Alive))
            {
                shooter.NextFireTurn = Turn + Options.ReloadTurns;
                var cursor = shooter.Position;
                var area = VisibleArea(shooter.Position);
                while (true)
                {
                    var next = cursor.Offset(dx, dy);
                    if (!area.Contains(next) || !Walkable(next)) break;
                    cursor = next;
                    var target = players.FirstOrDefault(p => p.Alive && p != shooter && p.Position == cursor);
                    if (target is not null) { playerHits.TryAdd(target, shooter); break; }
                    var enemy = enemies.FirstOrDefault(e => e.Position == cursor);
                    if (enemy is not null) { enemyHits.TryAdd(enemy, shooter); break; }
                }
                shots.Add(new(shooter.Id, shooter.Position, cursor));
            }
            foreach (var (target, shooter) in playerHits)
            {
                Kill(target, $"tir de {shooter.Bot.Identity.Name}");
                Award(shooter, 10);
            }
            foreach (var (target, shooter) in enemyHits)
            {
                enemies.Remove(target);
                Award(shooter, 5);
                AddEvent("kill", $"{shooter.Bot.Identity.Name} élimine un spectre (+5).");
            }
            MoveEnemies();
            if (Turn % Options.EnemySpawnEvery == 0 && enemies.Count < Options.MaxEnemies) SpawnEnemy();
            foreach (var player in players.Where(p => p.Alive)) { player.SurvivalTurns++; player.Score++; }
            if (Finished) AddEvent("finish", "La partie est terminée.");
            return Snapshot();
        }
        finally { stepLock.Release(); }
    }

    private static async Task<BotDecision> AskBotAsync(IGameBot bot, Observation observation, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            return await bot.DecideAsync(observation, timeout.Token).AsTask().WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(null, "délai de 3 secondes dépassé"); }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new(null, exception is System.Text.Json.JsonException ? "réponse JSON invalide" : "bot indisponible");
        }
    }

    public GameSnapshot Snapshot() => new(Id, Options, Turn, Finished,
        walls.OrderBy(p => p.Y).ThenBy(p => p.X).ToArray(),
        players.Select(p => new PlayerSnapshot(p.Id, p.Bot.Identity.Id, p.Bot.Identity.Name, p.Position, p.Alive,
            p.Score, p.Kills, p.Deaths, p.SurvivalTurns, Math.Max(0, p.NextFireTurn - Turn),
            p.Alive ? 0 : Math.Max(0, p.RespawnTurn - Turn), p.LastMove, p.LastError, p.Color)).ToArray(),
        enemies.Select(e => new EnemySnapshot(e.Id, e.Position, e.NeutralRemaining > 0, e.NeutralRemaining)).ToArray(),
        shots.ToArray(), events.TakeLast(60).ToArray());

    private void RespawnPlayers()
    {
        foreach (var player in players.Where(p => !p.Alive && Turn >= p.RespawnTurn))
        {
            if (players.Any(p => p.Alive && p.Position == player.Position) || enemies.Any(e => e.Position == player.Position)) continue;
            player.Alive = true;
            AddEvent("respawn", $"{player.Bot.Identity.Name} revient dans l'arène.");
        }
    }

    private void ResolveContact(Player player)
    {
        var enemy = enemies.FirstOrDefault(e => e.Position == player.Position);
        if (enemy is null) return;
        if (enemy.NeutralRemaining > 0)
        {
            enemies.Remove(enemy);
            Award(player, 5);
            AddEvent("kill", $"{player.Bot.Identity.Name} capture un spectre neutre (+5).");
        }
        else Kill(player, "capturé par un spectre");
    }

    private void MoveEnemies()
    {
        foreach (var enemy in enemies.ToArray())
        {
            (int X, int Y)[] offsets = [(0, -1), (0, 1), (-1, 0), (1, 0), (0, 0)];
            var targets = offsets.Select(d => enemy.Position.Offset(d.X, d.Y))
                .Where(p => Walkable(p) && !enemies.Any(e => e != enemy && e.Position == p)).ToArray();
            if (targets.Length > 0) enemy.Position = targets[random.Next(targets.Length)];
            var player = players.FirstOrDefault(p => p.Alive && p.Position == enemy.Position);
            if (player is not null) ResolveContact(player);
            if (enemy.NeutralRemaining > 0) enemy.NeutralRemaining--;
        }
    }

    private void Kill(Player player, string reason)
    {
        if (!player.Alive) return;
        player.Alive = false;
        player.Deaths++;
        player.RespawnTurn = Turn + Options.RespawnTurns + 1;
        AddEvent("death", $"{player.Bot.Identity.Name} : {reason}.");
    }
    private static void Award(Player player, int points) { player.Score += points; player.Kills++; }
    private void SpawnEnemy()
    {
        if (FreeCell() is { } position) enemies.Add(new(NextId(), position, Options.NeutralTurns));
    }
    private Position? FreeCell()
    {
        var occupied = players.Where(p => p.Alive).Select(p => p.Position).Concat(enemies.Select(e => e.Position)).ToHashSet();
        var free = new List<Position>();
        for (var y = 1; y < Options.Height - 1; y++)
            for (var x = 1; x < Options.Width - 1; x++)
            {
                var p = new Position(x, y);
                if (!walls.Contains(p) && !occupied.Contains(p)) free.Add(p);
            }
        return free.Count == 0 ? null : free[random.Next(free.Count)];
    }
    private bool Walkable(Position p) => p.X >= 0 && p.Y >= 0 && p.X < Options.Width && p.Y < Options.Height && !walls.Contains(p);
    private Area VisibleArea(Position p) => new(Math.Max(0, p.X - Options.VisionRadius), Math.Max(0, p.Y - Options.VisionRadius),
        Math.Min(Options.Width - 1, p.X + Options.VisionRadius), Math.Min(Options.Height - 1, p.Y + Options.VisionRadius));
    private Guid NextId() { var bytes = new byte[16]; random.NextBytes(bytes); return new(bytes); }
    private void AddEvent(string kind, string message)
    {
        events.Add(new(Turn, kind, message));
        if (events.Count > 100) events.RemoveRange(0, events.Count - 100);
    }
}
