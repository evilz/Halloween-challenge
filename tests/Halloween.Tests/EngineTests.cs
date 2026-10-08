using System.Diagnostics;
using System.Text.Json;
using Halloween.Engine;
using Xunit;

namespace Halloween.Tests;

public sealed class EngineTests
{
    private sealed class ScriptBot(string id, params string?[] moves) : IGameBot
    {
        private int index;
        public int Calls { get; private set; }
        public BotIdentity Identity { get; } = new(id, id, "test");
        public ValueTask<BotDecision> DecideAsync(Observation observation, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(new BotDecision(moves.Length == 0 ? null : moves[Math.Min(index++, moves.Length - 1)]));
        }
    }
    private sealed class TimeoutBot : IGameBot
    {
        public BotIdentity Identity { get; } = new("timeout", "timeout", "test");
        public async ValueTask<BotDecision> DecideAsync(Observation observation, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new(null);
        }
    }
    private static GameOptions Options => new() { Width = 10, Height = 10, MaxTurns = 50, InitialEnemies = 0, MaxEnemies = 0, EnemySpawnEvery = 1000 };
    private static GameEngine Scenario(IGameBot[] bots, Position[] positions, HashSet<Position>? walls = null,
        (Position, int)[]? enemies = null, GameOptions? options = null) => new(options ?? Options, bots, walls ?? [], positions, enemies ?? []);

    [Fact]
    public async Task PlayersEnteringSameCellBothDie()
    {
        var engine = Scenario([new ScriptBot("a", "right"), new ScriptBot("b", "left")], [new(1, 2), new(3, 2)]);
        var state = await engine.StepAsync();
        Assert.All(state.Players, p => Assert.False(p.Alive));
        Assert.All(state.Players, p => Assert.Equal(1, p.Deaths));
    }

    [Fact]
    public async Task MovingOntoStationaryPlayerKillsBoth()
    {
        var engine = Scenario([new ScriptBot("a", "right"), new ScriptBot("b")], [new(1, 2), new(2, 2)]);
        Assert.All((await engine.StepAsync()).Players, p => Assert.False(p.Alive));
    }

    [Fact]
    public async Task SwappingCellsIsAllowed()
    {
        var engine = Scenario([new ScriptBot("a", "right"), new ScriptBot("b", "left")], [new(1, 2), new(2, 2)]);
        var state = await engine.StepAsync();
        Assert.All(state.Players, p => Assert.True(p.Alive));
        Assert.Equal(new Position(2, 2), state.Players[0].Position);
        Assert.Equal(new Position(1, 2), state.Players[1].Position);
    }

    [Fact]
    public async Task ReciprocalShotsKillBothAndAwardBoth()
    {
        var engine = Scenario([new ScriptBot("a", "fire-right"), new ScriptBot("b", "fire-left")], [new(1, 2), new(3, 2)]);
        var state = await engine.StepAsync();
        Assert.All(state.Players, p => { Assert.False(p.Alive); Assert.Equal(10, p.Score); Assert.Equal(1, p.Kills); });
        Assert.Equal(2, state.Shots.Length);
    }

    [Fact]
    public async Task MovingBeforeShotsAllowsDodging()
    {
        var engine = Scenario([new ScriptBot("a", "fire-right"), new ScriptBot("b", "down")], [new(1, 2), new(3, 2)]);
        Assert.All((await engine.StepAsync()).Players, p => Assert.True(p.Alive));
    }

    [Fact]
    public async Task ShotsCanHitAPlayerWhoMovedIntoTheirPath()
    {
        var engine = Scenario([new ScriptBot("a", "fire-right"), new ScriptBot("b", "up")], [new(1, 2), new(3, 3)]);
        Assert.False((await engine.StepAsync()).Players[1].Alive);
    }

    [Fact]
    public async Task WallsBlockShots()
    {
        var engine = Scenario([new ScriptBot("a", "fire-right"), new ScriptBot("b")], [new(1, 2), new(3, 2)], [new(2, 2)]);
        var state = await engine.StepAsync();
        Assert.True(state.Players[1].Alive);
        Assert.Equal(new Position(1, 2), state.Shots[0].To);
    }

    [Fact]
    public async Task ShotsStopAtFirstTarget()
    {
        var engine = Scenario([new ScriptBot("a", "fire-right"), new ScriptBot("b"), new ScriptBot("c")], [new(1, 2), new(2, 2), new(3, 2)]);
        var state = await engine.StepAsync();
        Assert.False(state.Players[1].Alive);
        Assert.True(state.Players[2].Alive);
    }

    [Fact]
    public async Task ShotsCannotExceedVisibleRange()
    {
        var engine = Scenario([new ScriptBot("a", "fire-right"), new ScriptBot("b")], [new(1, 2), new(5, 2)], options: Options with { VisionRadius = 2 });
        var state = await engine.StepAsync();
        Assert.True(state.Players[1].Alive);
        Assert.Equal(new Position(3, 2), state.Shots[0].To);
    }

    [Fact]
    public async Task ReloadBlocksExactlyConfiguredFollowingTurns()
    {
        var engine = Scenario([new ScriptBot("a", "fire-right")], [new(1, 2)], options: Options with { ReloadTurns = 2 });
        Assert.Single((await engine.StepAsync()).Shots);
        Assert.False(engine.Observe(engine.Snapshot().Players[0].Id).Player.Fire);
        Assert.Empty((await engine.StepAsync()).Shots);
        Assert.Empty((await engine.StepAsync()).Shots);
        Assert.True(engine.Observe(engine.Snapshot().Players[0].Id).Player.Fire);
        Assert.Single((await engine.StepAsync()).Shots);
    }

    [Fact]
    public async Task NeutralEnemyDiesOnContactAndAwardsPoints()
    {
        var engine = Scenario([new ScriptBot("a", "right")], [new(1, 2)], enemies: [(new(2, 2), 3)]);
        var state = await engine.StepAsync();
        Assert.Empty(state.Enemies);
        Assert.True(state.Players[0].Alive);
        Assert.Equal(6, state.Players[0].Score);
    }

    [Fact]
    public async Task HostileEnemyKillsPlayerOnContact()
    {
        var engine = Scenario([new ScriptBot("a", "right")], [new(1, 2)], enemies: [(new(2, 2), 0)]);
        Assert.False((await engine.StepAsync()).Players[0].Alive);
    }

    [Fact]
    public async Task EnemyIsShotBeforeItCanMove()
    {
        var engine = Scenario([new ScriptBot("a", "fire-right")], [new(1, 2)], enemies: [(new(2, 2), 0)]);
        var state = await engine.StepAsync();
        Assert.Empty(state.Enemies);
        Assert.Equal(6, state.Players[0].Score);
    }

    [Fact]
    public async Task NeutralEnemyBecomesHostileAfterItsGracePeriod()
    {
        var engine = Scenario([new ScriptBot("a")], [new(1, 2)], enemies: [(new(8, 8), 2)]);
        Assert.True(Assert.Single((await engine.StepAsync()).Enemies).Neutral);
        Assert.False(Assert.Single((await engine.StepAsync()).Enemies).Neutral);
    }

    [Fact]
    public async Task DeadPlayerWaitsAndRespawnsAtDeathPosition()
    {
        var victim = new ScriptBot("b");
        var engine = Scenario([new ScriptBot("a", "fire-right", null), victim], [new(1, 2), new(3, 2)], options: Options with { RespawnTurns = 2 });
        Assert.False((await engine.StepAsync()).Players[1].Alive);
        Assert.False((await engine.StepAsync()).Players[1].Alive);
        Assert.False((await engine.StepAsync()).Players[1].Alive);
        var state = await engine.StepAsync();
        Assert.True(state.Players[1].Alive);
        Assert.Equal(new Position(3, 2), state.Players[1].Position);
        Assert.Equal(1, victim.Calls);
    }

    [Fact]
    public async Task OccupiedDeathCellDelaysRespawn()
    {
        var engine = Scenario([new ScriptBot("a", "fire-right", "right", "right", null), new ScriptBot("b")],
            [new(1, 2), new(3, 2)], options: Options with { RespawnTurns = 2 });
        await engine.StepAsync(); await engine.StepAsync(); await engine.StepAsync();
        var state = await engine.StepAsync();
        Assert.False(state.Players[1].Alive);
        Assert.True(state.Players[0].Alive);
    }

    [Fact]
    public async Task WallsAndBoardEdgesPreventMovement()
    {
        var engine = Scenario([new ScriptBot("a", "left", "up")], [new(0, 1)], [new(0, 0)]);
        await engine.StepAsync();
        Assert.Equal(new Position(0, 1), (await engine.StepAsync()).Players[0].Position);
    }

    [Fact]
    public void ObservationContainsOnlyVisibleEntitiesAndWireContract()
    {
        var engine = Scenario([new ScriptBot("a"), new ScriptBot("b"), new ScriptBot("c")],
            [new(1, 1), new(2, 1), new(8, 8)], [new(2, 2), new(8, 7)], [(new(2, 3), 2), (new(7, 8), 0)],
            Options with { VisionRadius = 2 });
        var observation = engine.Observe(engine.Snapshot().Players[0].Id);
        Assert.Single(observation.Players); Assert.Single(observation.Enemies); Assert.Single(observation.Board.Walls);
        Assert.Equal(new Area(0, 0, 3, 3), observation.Player.Area);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(observation, JsonSerializerOptions.Web));
        Assert.True(json.RootElement.GetProperty("player").GetProperty("fire").GetBoolean());
        Assert.Equal(10, json.RootElement.GetProperty("board").GetProperty("size").GetProperty("width").GetInt32());
    }

    [Fact]
    public async Task SameSeedAndBuiltinBotsProduceSameSimulation()
    {
        GameEngine Create() => new(Options with { InitialEnemies = 4, MaxEnemies = 10, EnemySpawnEvery = 8 },
            BuiltinBot.Catalog.Select((b, i) => (IGameBot)new BuiltinBot(b.Id, 2020 + i * 7919)).ToArray());
        var first = Create(); var second = Create();
        Assert.Equal(first.Snapshot().Walls, second.Snapshot().Walls);
        for (var i = 0; i < 50; i++)
        {
            var a = await first.StepAsync();var b = await second.StepAsync();
            Assert.Equal(a.Players.Select(p => (p.Position,p.Alive,p.Score)), b.Players.Select(p => (p.Position,p.Alive,p.Score)));
            Assert.Equal(a.Enemies.Select(e => (e.Position,e.Neutral)), b.Enemies.Select(e => (e.Position,e.Neutral)));
        }
    }

    [Theory]
    [InlineData(10, 10)] [InlineData(50, 25)] [InlineData(24, 16)]
    public void AllGeneratedFloorCellsAreConnected(int width, int height)
    {
        var walls = Maze.Generate(width, height, new Random(123));
        var seen = new HashSet<Position> { new(1, 1) };var queue = new Queue<Position>();queue.Enqueue(new(1,1));
        while (queue.TryDequeue(out var p))
            foreach (var next in new[] { p.Offset(1,0),p.Offset(-1,0),p.Offset(0,1),p.Offset(0,-1) })
                if (next.X >= 0 && next.Y >= 0 && next.X < width && next.Y < height && !walls.Contains(next) && seen.Add(next)) queue.Enqueue(next);
        Assert.Equal(width * height - walls.Count, seen.Count);
    }

    [Fact]
    public async Task ConcurrentTimeoutsCostOneDeadlineAndAreNullMoves()
    {
        var engine = Scenario([new TimeoutBot(),new TimeoutBot(),new TimeoutBot()], [new(1,1),new(3,3),new(5,5)]);
        var stopwatch = Stopwatch.StartNew();var state = await engine.StepAsync();
        Assert.InRange(stopwatch.Elapsed.TotalSeconds, 2.8, 5.5);
        Assert.All(state.Players, p => { Assert.True(p.Alive); Assert.NotNull(p.LastError); Assert.Null(p.LastMove); });
    }

    [Fact]
    public async Task CancellationLeavesTurnUnchanged()
    {
        var engine = Scenario([new TimeoutBot()], [new(1,1)]);
        using var cancellation = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.StepAsync(cancellation.Token));
        Assert.Equal(0, engine.Turn);
    }

    [Fact]
    public async Task FinishedGameDoesNotAdvance()
    {
        var bot = new ScriptBot("a");var engine = Scenario([bot], [new(1,1)]);
        for (var i=0;i<55;i++) await engine.StepAsync();
        Assert.Equal(50, engine.Turn); Assert.Equal(50, bot.Calls); Assert.True(engine.Finished);
    }

    [Fact]
    public async Task ManyGamesMaintainSingleOccupancyAndEnemyLimit()
    {
        for (var seed=0;seed<8;seed++)
        {
            var options=Options with { Seed=seed,InitialEnemies=12,MaxEnemies=20,EnemySpawnEvery=1,RespawnTurns=1 };
            var bots=Enumerable.Range(0,9).Select(i=>(IGameBot)new CustomIdentityBot(new BuiltinBot(BuiltinBot.Catalog[i%3].Id,seed+i),i)).ToArray();
            var engine=new GameEngine(options,bots);
            for (var turn=0;turn<50;turn++)
            {
                var state=await engine.StepAsync();
                var occupied=state.Players.Where(p=>p.Alive).Select(p=>p.Position).Concat(state.Enemies.Select(e=>e.Position)).ToArray();
                Assert.Equal(occupied.Length,occupied.Distinct().Count());Assert.True(state.Enemies.Length<=20);
                Assert.DoesNotContain(occupied,p=>state.Walls.Contains(p));
            }
        }
    }
    private sealed class CustomIdentityBot(IGameBot bot,int id) : IGameBot
    {
        public BotIdentity Identity { get; } = bot.Identity with { Id=id.ToString() };
        public ValueTask<BotDecision> DecideAsync(Observation observation,CancellationToken ct) => bot.DecideAsync(observation,ct);
    }
}
