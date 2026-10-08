using System.Text.Json;
using Halloween.Engine;

if (args.Contains("--help"))
{
    Console.WriteLine("Halloween.Simulator [--seed 2020] [--turns 150] [--games 1] [--output replay.json]");
    return;
}
int ReadNumber(string name, int fallback)
{
    var index = Array.IndexOf(args, name);
    return index < 0 ? fallback : index + 1 < args.Length && int.TryParse(args[index + 1], out var value)
        ? value : throw new ArgumentException($"Valeur invalide pour {name}.");
}
try
{
    var seed = ReadNumber("--seed", 2020);
    var turns = ReadNumber("--turns", 150);
    var games = ReadNumber("--games", 1);
    if (games is < 1 or > 1000) throw new ArgumentException("--games doit être compris entre 1 et 1000.");
    var outputIndex = Array.IndexOf(args, "--output");
    if (outputIndex >= 0 && outputIndex + 1 >= args.Length) throw new ArgumentException("Le chemin --output est obligatoire.");
    var totals = BuiltinBot.Catalog.ToDictionary(b => b.Name, _ => 0L);
    for (var game = 0; game < games; game++)
    {
        var options = new GameOptions { Seed = unchecked(seed + game), MaxTurns = turns };
        var bots = BuiltinBot.Catalog.Select((b, i) => (IGameBot)new BuiltinBot(b.Id, unchecked(options.Seed + i * 7919))).ToArray();
        var engine = new GameEngine(options, bots);
        var state = engine.Snapshot();
        var output = outputIndex >= 0 && game == games - 1;
        // Stream long replays instead of retaining thousands of boards in memory.
        await using var stream = output ? File.Create(args[outputIndex + 1]) : null;
        using var writer = stream is null ? null : new Utf8JsonWriter(stream);
        writer?.WriteStartObject();
        writer?.WriteNumber("version", 1);
        writer?.WritePropertyName("frames");
        writer?.WriteStartArray();
        if (writer is not null) JsonSerializer.Serialize(writer, state, JsonSerializerOptions.Web);
        while (!state.Finished)
        {
            state = await engine.StepAsync();
            if (writer is not null) JsonSerializer.Serialize(writer, state, JsonSerializerOptions.Web);
        }
        writer?.WriteEndArray();
        writer?.WriteEndObject();
        if (writer is not null) await writer.FlushAsync();
        Console.WriteLine($"Partie {game + 1} | graine {options.Seed} | {state.Turn} tours");
        foreach (var player in state.Players.OrderByDescending(p => p.Score))
        {
            totals[player.Name] += player.Score;
            Console.WriteLine($"  {player.Name,-24} {player.Score,5} pts | {player.Kills,3} éliminations | {player.Deaths,3} morts");
        }
    }
    if (games > 1)
        foreach (var (name, score) in totals.OrderByDescending(p => p.Value))
            Console.WriteLine($"Moyenne {name,-24} {(double)score / games:F1} pts");
}
catch (ArgumentException error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }
