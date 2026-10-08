using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Halloween.Engine;
using Halloween.Web;
using Halloween.Web.Bots;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 32_768);
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<BotRegistry>();
builder.Services.AddSingleton<MatchStore>();
builder.Services.AddHttpClient("bots", client =>
{
    client.Timeout = TimeSpan.FromSeconds(3);
    client.MaxResponseContentBufferSize = 16_384;
}).ConfigurePrimaryHttpMessageHandler(() => BotHttp.CreateHandler(builder.Configuration.GetValue<bool>("Bots:AllowLoopback")));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddConcurrencyLimiter("simulation", limiter => { limiter.PermitLimit = 8; limiter.QueueLimit = 8; });
});
var app = builder.Build();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'";
    await next(context);
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.MapGet("/health", () => TypedResults.Ok(new { status = "ok", framework = ".NET 10", protocol = "vpTech 2020" }));
var api = app.MapGroup("/api");
api.MapGet("/bots", async (BotRegistry registry, CancellationToken ct) => TypedResults.Ok(await registry.ListAsync(ct)));
api.MapPost("/bots", async (RegisterBotRequest request, BotRegistry registry, CancellationToken ct) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(request.Url)) return (IResult)TypedResults.BadRequest(new { error = "L'URL du bot est obligatoire." });
        var bot = await registry.RegisterAsync(request.Url, ct);
        return TypedResults.Created($"/api/bots/{bot.Id}", bot);
    }
    catch (ArgumentException e) { return TypedResults.BadRequest(new { error = e.Message }); }
    catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException)
    { return TypedResults.BadRequest(new { error = "Le bot ne répond pas correctement à POST /name (3 secondes, JSON { name, email }). Vérifiez aussi que son adresse est autorisée." }); }
});
api.MapPost("/matches", async (CreateMatchRequest request, BotRegistry registry, MatchStore store, CancellationToken ct) =>
{
    try
    {
        var options = request.Options ?? new GameOptions();
        if (options.Validate() is { } error) return (IResult)TypedResults.BadRequest(new { error });
        var bots = await registry.ResolveAsync(request.BotIds ?? BuiltinBot.Catalog.Select(b => b.Id).ToArray(), options.Seed, ct);
        var match = store.Create(options, bots);
        return TypedResults.Created($"/api/matches/{match.State.Id}", match.State);
    }
    catch (ArgumentException e) { return TypedResults.BadRequest(new { error = e.Message }); }
}).RequireRateLimiting("simulation");
api.MapGet("/matches/{id:guid}", (Guid id, MatchStore store) => store.Find(id) is { } match
    ? (IResult)TypedResults.Ok(match.State) : TypedResults.NotFound());
api.MapPost("/matches/{id:guid}/step", async (Guid id, MatchStore store, CancellationToken ct) => store.Find(id) is { } match
    ? (IResult)TypedResults.Ok(await match.StepAsync(ct)) : TypedResults.NotFound()).RequireRateLimiting("simulation");
api.MapDelete("/matches/{id:guid}", (Guid id, MatchStore store) => store.Remove(id)
    ? (IResult)TypedResults.NoContent() : TypedResults.NotFound());
app.Run();

public partial class Program;
