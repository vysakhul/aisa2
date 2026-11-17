using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Aisa.Nlu;

namespace Aisa.Nlu.Tests;

public class PipelineTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 15, 0, TimeSpan.FromHours(5.5));

    sealed class H(string? content, bool fail = false) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            if (fail) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }))
            });
        }
    }

    const string ListJson = "{\"kind\":\"list\",\"when_local\":null,\"label\":null,\"cancel_query\":null}";
    const string UnknownJson = "{\"kind\":\"unknown\",\"when_local\":null,\"label\":null,\"cancel_query\":null}";

    static (NluPipeline p, H h) Make(string? json, bool fail = false)
    {
        var h = new H(json, fail);
        return (new NluPipeline(new LmStudioClient(new LmStudioOptions(), new FakeTimeProvider(Now), h)), h);
    }

    [Fact]
    public async Task ConfidentParseSkipsLlm()
    {
        var (p, h) = Make(ListJson);
        var r = await p.ParseAsync("remind me to call mom at 5pm", Now);
        Assert.False(r.UsedLlm);
        Assert.Equal(0, h.Calls);
    }

    [Fact]
    public async Task UnknownCallsLlm()
    {
        var (p, h) = Make(ListJson);
        var r = await p.ParseAsync("what do I have on", Now);
        Assert.True(r.UsedLlm);
        Assert.Equal(IntentKind.List, r.Intent.Kind);
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task LlmUnknownIsReturnedWithUsedLlm()
    {
        var (p, _) = Make(UnknownJson);
        var r = await p.ParseAsync("blargh", Now);
        Assert.True(r.UsedLlm);
        Assert.Equal(IntentKind.Unknown, r.Intent.Kind);
    }

    [Fact]
    public async Task NeedsTimeButLooksTimedCallsLlm()
    {
        var (p, h) = Make("{\"kind\":\"set_reminder\",\"when_local\":\"2026-11-19T09:00\",\"label\":\"Dentist\",\"cancel_query\":null}");
        var r = await p.ParseAsync("remind me about the dentist at 25:99", Now);
        Assert.True(r.UsedLlm);
        Assert.Equal(1, h.Calls);
        Assert.Equal(9, r.Intent.When!.Value.Hour);
    }

    [Fact]
    public async Task NeedsTimeWithoutTimeWordsSkipsLlm()
    {
        var (p, h) = Make(ListJson);
        var r = await p.ParseAsync("remind me to call mom", Now);
        Assert.False(r.UsedLlm);
        Assert.True(r.Intent.NeedsTime);
        Assert.Equal(0, h.Calls);
    }

    [Fact]
    public async Task TimeWordsInLabelCallLlm()
    {
        var (p, h) = Make("{\"kind\":\"set_reminder\",\"when_local\":\"2026-11-17T17:00\",\"label\":\"Buy apples\",\"cancel_query\":null}");
        var r = await p.ParseAsync("remind me to buy 2 apples at 5pm", Now);
        Assert.True(r.UsedLlm);
        Assert.Equal("Buy apples", r.Intent.Label);
        Assert.Equal(1, h.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LlmFailureFallsBackToDeterministic(bool badJson)
    {
        var (p, _) = Make(badJson ? "nope" : null, fail: !badJson);
        var r = await p.ParseAsync("remind me to buy 2 apples at 5pm", Now);
        Assert.False(r.UsedLlm);
        Assert.Equal(IntentKind.SetReminder, r.Intent.Kind);
        Assert.Equal("Buy 2 apples", r.Intent.Label);
    }

    [Fact]
    public async Task NullClientWorks()
    {
        var r = await new NluPipeline(null).ParseAsync("blargh", Now);
        Assert.False(r.UsedLlm);
        Assert.Equal(IntentKind.Unknown, r.Intent.Kind);
    }
}
