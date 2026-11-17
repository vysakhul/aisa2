using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Aisa.Nlu;

namespace Aisa.Nlu.Tests;

public class LmStudioTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 15, 0, TimeSpan.FromHours(5.5));

    sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn) : HttpMessageHandler
    {
        public int Calls;
        public string? Body;
        public Uri? Uri;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            Uri = r.RequestUri;
            Body = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);
            return await fn(r, ct);
        }
    }

    static HttpResponseMessage Ok(string contentJson) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { role = "assistant", content = contentJson } } }
        }), Encoding.UTF8, "application/json")
    };

    static (LmStudioClient c, FakeHandler h, FakeTimeProvider t) Make(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn, Action<LmStudioOptions>? cfg = null)
    {
        var o = new LmStudioOptions();
        cfg?.Invoke(o);
        var t = new FakeTimeProvider(Now);
        var h = new FakeHandler(fn);
        return (new LmStudioClient(o, t, h), h, t);
    }

    [Fact]
    public async Task Success()
    {
        var (c, h, _) = Make((_, _) => Task.FromResult(Ok("{\"kind\":\"set_reminder\",\"when_local\":\"2026-11-18T09:30\",\"label\":\"call mom\",\"cancel_query\":null}")));
        var r = await c.TryParseAsync("call mom day after tomorrow-ish", Now, null);
        Assert.Equal(new ParsedIntent(IntentKind.SetReminder, new DateTimeOffset(2026, 11, 18, 9, 30, 0, Now.Offset), "Call mom"), r);
        Assert.Equal("http://localhost:1234/v1/chat/completions", h.Uri!.ToString());
        Assert.False(c.IsBackingOff);
    }

    [Fact]
    public async Task RequestBodyHasStrictSchemaAndContext()
    {
        var (c, h, _) = Make((_, _) => Task.FromResult(Ok("{\"kind\":\"unknown\",\"when_local\":null,\"label\":null,\"cancel_query\":null}")),
            o => o.Model = "qwen");
        await c.TryParseAsync("blah", Now, new ParsedIntent(IntentKind.SetAlarm, Label: "Alarm"));
        using var d = JsonDocument.Parse(h.Body!);
        var root = d.RootElement;
        Assert.Equal("qwen", root.GetProperty("model").GetString());
        Assert.Equal(0, root.GetProperty("temperature").GetInt32());
        var rf = root.GetProperty("response_format");
        Assert.Equal("json_schema", rf.GetProperty("type").GetString());
        var js = rf.GetProperty("json_schema");
        Assert.True(js.GetProperty("strict").GetBoolean());
        var props = js.GetProperty("schema").GetProperty("properties");
        foreach (var p in new[] { "kind", "when_local", "label", "cancel_query" }) Assert.True(props.TryGetProperty(p, out _));
        Assert.Equal(7, props.GetProperty("kind").GetProperty("enum").GetArrayLength());
        var msgs = root.GetProperty("messages");
        var sys = msgs[0].GetProperty("content").GetString()!;
        Assert.Contains("Tuesday 2026-11-17 10:15", sys);
        Assert.Contains("+05:30", sys);
        Assert.Contains("yyyy-MM-ddTHH:mm", sys);
        var user = msgs[1].GetProperty("content").GetString()!;
        Assert.Contains("blah", user);
        Assert.Contains("set_alarm", user);
    }

    [Fact]
    public async Task ModelOmittedWhenNull()
    {
        var (c, h, _) = Make((_, _) => Task.FromResult(Ok("{\"kind\":\"help\",\"when_local\":null,\"label\":null,\"cancel_query\":null}")));
        var r = await c.TryParseAsync("x", Now, null);
        Assert.Equal(IntentKind.Help, r!.Kind);
        Assert.False(JsonDocument.Parse(h.Body!).RootElement.TryGetProperty("model", out _));
    }

    [Fact]
    public async Task PastWhenIsDropped()
    {
        var (c, _, _) = Make((_, _) => Task.FromResult(Ok("{\"kind\":\"set_alarm\",\"when_local\":\"2026-11-17T08:00\",\"label\":null,\"cancel_query\":null}")));
        var r = await c.TryParseAsync("x", Now, null);
        Assert.True(r!.NeedsTime);
        Assert.Equal("Alarm", r.Label);
    }

    [Fact]
    public async Task MalformedJsonReturnsNullAndBacksOff()
    {
        var (c, h, _) = Make((_, _) => Task.FromResult(Ok("not json")));
        Assert.Null(await c.TryParseAsync("x", Now, null));
        Assert.True(c.IsBackingOff);
        Assert.Null(await c.TryParseAsync("x", Now, null));
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task NonSuccessBacksOff()
    {
        var (c, _, _) = Make((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        Assert.Null(await c.TryParseAsync("x", Now, null));
        Assert.True(c.IsBackingOff);
    }

    [Fact]
    public async Task ConnectionFailureBacksOff()
    {
        var (c, _, _) = Make((_, _) => throw new HttpRequestException("refused"));
        Assert.Null(await c.TryParseAsync("x", Now, null));
        Assert.True(c.IsBackingOff);
    }

    [Fact]
    public async Task TimeoutReturnsNullAndBacksOff()
    {
        var (c, h, t) = Make(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Ok("{}"); });
        var task = c.TryParseAsync("x", Now, null);
        while (h.Calls == 0) await Task.Delay(5);
        t.Advance(TimeSpan.FromSeconds(15));
        Assert.Null(await task);
        Assert.True(c.IsBackingOff);
    }

    [Fact]
    public async Task BackoffExpiresAfter30Seconds()
    {
        var n = 0;
        var (c, h, t) = Make((_, _) => Task.FromResult(n++ == 0
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : Ok("{\"kind\":\"list\",\"when_local\":null,\"label\":null,\"cancel_query\":null}")));
        Assert.Null(await c.TryParseAsync("x", Now, null));
        t.Advance(TimeSpan.FromSeconds(29));
        Assert.Null(await c.TryParseAsync("x", Now, null));
        Assert.Equal(1, h.Calls);
        t.Advance(TimeSpan.FromSeconds(2));
        Assert.False(c.IsBackingOff);
        Assert.Equal(IntentKind.List, (await c.TryParseAsync("x", Now, null))!.Kind);
    }

    [Fact]
    public async Task DisabledMakesNoCall()
    {
        var (c, h, _) = Make((_, _) => Task.FromResult(Ok("{}")), o => o.Enabled = false);
        Assert.Null(await c.TryParseAsync("x", Now, null));
        Assert.Equal(0, h.Calls);
        c.Options.Enabled = true;
        await c.TryParseAsync("x", Now, null);
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        var (c, _, _) = Make(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Ok("{}"); });
        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => c.TryParseAsync("x", Now, null, cts.Token));
    }
}
