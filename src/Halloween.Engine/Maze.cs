namespace Halloween.Engine;

public static class Maze
{
    // Randomized depth-first carving keeps every floor cell reachable.
    public static HashSet<Position> Generate(int width, int height, Random random)
    {
        var walls = new HashSet<Position>();
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++) walls.Add(new(x, y));
        var stack = new Stack<Position>();
        stack.Push(new(1, 1));
        walls.Remove(stack.Peek());
        (int X, int Y)[] directions = [(0, -2), (2, 0), (0, 2), (-2, 0)];
        while (stack.TryPeek(out var current))
        {
            var candidates = directions.Select(d => current.Offset(d.X, d.Y))
                .Where(p => p.X > 0 && p.Y > 0 && p.X < width - 1 && p.Y < height - 1 && walls.Contains(p)).ToArray();
            if (candidates.Length == 0) { stack.Pop(); continue; }
            var next = candidates[random.Next(candidates.Length)];
            walls.Remove(current.Offset((next.X - current.X) / 2, (next.Y - current.Y) / 2));
            walls.Remove(next);
            stack.Push(next);
        }
        // Add loops for dodging and encounters without isolating corridors.
        foreach (var wall in walls.OrderBy(p => p.Y).ThenBy(p => p.X).ToArray())
        {
            if (wall.X <= 0 || wall.Y <= 0 || wall.X >= width - 1 || wall.Y >= height - 1) continue;
            var horizontal = !walls.Contains(wall.Offset(-1, 0)) && !walls.Contains(wall.Offset(1, 0));
            var vertical = !walls.Contains(wall.Offset(0, -1)) && !walls.Contains(wall.Offset(0, 1));
            if ((horizontal || vertical) && random.NextDouble() < .24) walls.Remove(wall);
        }
        return walls;
    }
}
