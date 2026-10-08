namespace Halloween.Engine;

public readonly record struct Position(int X, int Y)
{
    public Position Offset(int x, int y) => new(X + x, Y + y);
}

public sealed record GameOptions
{
    public int Width { get; init; } = 25;
    public int Height { get; init; } = 17;
    public int MaxTurns { get; init; } = 150;
    public int Seed { get; init; } = 2020;
    public int VisionRadius { get; init; } = 3;
    public int ReloadTurns { get; init; } = 3;
    public int RespawnTurns { get; init; } = 5;
    public int InitialEnemies { get; init; } = 8;
    public int EnemySpawnEvery { get; init; } = 8;
    public int NeutralTurns { get; init; } = 3;
    public int MaxEnemies { get; init; } = 30;

    public string? Validate() => this switch
    {
        { Width: < 10 or > 50 } => "La largeur doit être comprise entre 10 et 50.",
        { Height: < 10 or > 25 } => "La hauteur doit être comprise entre 10 et 25.",
        { MaxTurns: < 50 or > 5000 } => "La durée doit être comprise entre 50 et 5000 tours.",
        { VisionRadius: < 1 or > 10 } => "La vision doit être comprise entre 1 et 10 cases.",
        { ReloadTurns: < 0 or > 50 } => "Le rechargement doit être compris entre 0 et 50 tours.",
        { RespawnTurns: < 0 or > 50 } => "La réapparition doit être comprise entre 0 et 50 tours.",
        { EnemySpawnEvery: < 1 or > 1000 } => "La fréquence des ennemis doit être comprise entre 1 et 1000.",
        { NeutralTurns: < 0 or > 50 } => "La neutralité doit être comprise entre 0 et 50 tours.",
        { MaxEnemies: < 0 or > 100 } => "Le nombre maximal d'ennemis doit être compris entre 0 et 100.",
        { InitialEnemies: < 0 } => "Le nombre initial d'ennemis doit être positif ou nul.",
        _ when InitialEnemies > MaxEnemies => "Le nombre initial dépasse le maximum d'ennemis.",
        _ => null
    };
}

public sealed record BotIdentity(string Id, string Name, string Kind, string? Url = null);
public sealed record Area(int X1, int Y1, int X2, int Y2)
{
    public bool Contains(Position p) => p.X >= X1 && p.X <= X2 && p.Y >= Y1 && p.Y <= Y2;
}
public sealed record GameInfo(Guid Id);
public sealed record PlayerView(Guid Id, string Name, Position Position, Position Previous, Area Area, bool Fire);
public sealed record BoardSize(int Width, int Height);
public sealed record BoardView(BoardSize Size, Position[] Walls);
public sealed record EnemyView(int X, int Y, bool Neutral);
// Wire format follows the /move contract in the PDF, with real JSON booleans.
public sealed record Observation(GameInfo Game, PlayerView Player, BoardView Board, Position[] Players, EnemyView[] Enemies);
public sealed record MoveResponse(string? Move);
public sealed record NameResponse(string Name, string? Email);
public sealed record BotDecision(string? Move, string? Error = null);
public interface IGameBot
{
    BotIdentity Identity { get; }
    ValueTask<BotDecision> DecideAsync(Observation observation, CancellationToken cancellationToken);
}

public sealed record PlayerSnapshot(Guid Id, string BotId, string Name, Position Position, bool Alive,
    int Score, int Kills, int Deaths, int SurvivalTurns, int ReloadRemaining, int RespawnRemaining,
    string? LastMove, string? LastError, string Color);
public sealed record EnemySnapshot(Guid Id, Position Position, bool Neutral, int NeutralRemaining);
public sealed record ShotSnapshot(Guid ShooterId, Position From, Position To);
public sealed record GameEvent(int Turn, string Kind, string Message);
public sealed record GameSnapshot(Guid Id, GameOptions Options, int Turn, bool Finished,
    Position[] Walls, PlayerSnapshot[] Players, EnemySnapshot[] Enemies, ShotSnapshot[] Shots, GameEvent[] Events);

public static class Commands
{
    public static readonly string[] Moves = ["up", "down", "left", "right", "fire-up", "fire-down", "fire-left", "fire-right"];
    public static bool TryParse(string? command, out int dx, out int dy, out bool fire)
    {
        fire = command?.StartsWith("fire-", StringComparison.Ordinal) == true;
        var direction = fire ? command![5..] : command;
        (dx, dy) = direction switch { "up" => (0, -1), "down" => (0, 1), "left" => (-1, 0), "right" => (1, 0), _ => (0, 0) };
        return dx != 0 || dy != 0;
    }
}
