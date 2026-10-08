using System.Net;
using System.Text;
using Halloween.Engine;
using Halloween.Web.Bots;
using Xunit;

namespace Halloween.Tests;

public sealed class BotHttpTests
{
    [Theory]
    [InlineData("127.0.0.1")] [InlineData("10.0.0.1")] [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")] [InlineData("169.254.169.254")] [InlineData("100.64.0.1")]
    [InlineData("::1")] [InlineData("::ffff:127.0.0.1")] [InlineData("fc00::1")]
    [InlineData("fe80::1")] [InlineData("2001:db8::1")] [InlineData("198.18.0.1")]
    public void PrivateAndReservedAddressesAreRejected(string ip) => Assert.False(BotHttp.IsPublicAddress(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("8.8.8.8")] [InlineData("1.1.1.1")] [InlineData("2606:4700:4700::1111")]
    public void PublicAddressesAreAccepted(string ip) => Assert.True(BotHttp.IsPublicAddress(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("file:///etc/passwd")] [InlineData("https://user:secret@example.com")]
    [InlineData("http://example.com/?x=1")] [InlineData("not-a-url")]
    public void UnsafeBaseUrlsAreRejected(string url) => Assert.Throws<ArgumentException>(() => BotHttp.ParseBaseUrl(url));

    [Fact]
    public void BasePathIsPreserved() => Assert.Equal("https://example.com/my-bot/move", new Uri(BotHttp.ParseBaseUrl("https://example.com/my-bot"), "move").AbsoluteUri);

    private sealed class FakeHandler(string json) : HttpMessageHandler
    {
        public Uri? Requested { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Requested=request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(json,Encoding.UTF8,"application/json") });
        }
    }

    [Theory]
    [InlineData("{\"move\":\"right\"}","right",false)]
    [InlineData("{\"move\":\"teleport\"}",null,true)]
    [InlineData("{\"move\":null}",null,false)]
    public async Task RemoteBotHandlesValidInvalidAndNullMoves(string json,string? expected,bool error)
    {
        var handler=new FakeHandler(json);using var client=new HttpClient(handler);
        var bot=new RemoteBot(new("a","a","remote","https://example.com/bot/"),client);
        var engine=new GameEngine(new() { InitialEnemies=0 },[new BuiltinBot("hunter",1)]);
        var decision=await bot.DecideAsync(engine.Observe(engine.Snapshot().Players[0].Id),CancellationToken.None);
        Assert.Equal(expected,decision.Move);Assert.Equal(error,decision.Error is not null);
        Assert.Equal("https://example.com/bot/move",handler.Requested!.AbsoluteUri);
    }
}
