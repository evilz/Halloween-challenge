using Halloween.Engine;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapPost("/name", () => TypedResults.Ok(new NameResponse("Mon premier bot", "bot@example.com")));
app.MapPost("/move", (Observation state) =>
{
    // Replace this function with your strategy. The server sends only visible cells.
    var walls = state.Board.Walls.ToHashSet();
    var p = state.Player.Position;
    (string Name, int X, int Y)[] directions = [("up", 0, -1), ("right", 1, 0), ("down", 0, 1), ("left", -1, 0)];
    if (state.Player.Fire)
        foreach (var d in directions)
        {
            var target = p.Offset(d.X, d.Y);
            while (state.Player.Area.Contains(target) && !walls.Contains(target))
            {
                if (state.Enemies.Any(e => e.X == target.X && e.Y == target.Y) || state.Players.Contains(target))
                    return TypedResults.Ok(new MoveResponse($"fire-{d.Name}"));
                target = target.Offset(d.X, d.Y);
            }
        }
    var valid = directions.Where(d =>
    {
        var target = p.Offset(d.X, d.Y);
        return target.X >= 0 && target.Y >= 0 && target.X < state.Board.Size.Width && target.Y < state.Board.Size.Height
            && !walls.Contains(target) && !state.Players.Contains(target)
            && !state.Enemies.Any(e => !e.Neutral && e.X == target.X && e.Y == target.Y);
    }).ToArray();
    var forward = valid.Where(d => p.Offset(d.X, d.Y) != state.Player.Previous).ToArray();
    var choices = forward.Length > 0 ? forward : valid;
    return TypedResults.Ok(new MoveResponse(choices.Length == 0 ? null : choices[Random.Shared.Next(choices.Length)].Name));
});
app.Run();
