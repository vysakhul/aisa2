using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aisa.Nlu;

public sealed class LmStudioOptions
{
    public bool Enabled { get; set; } = true;
    public Uri BaseUrl { get; set; } = new("http://localhost:1234/v1/");
    public string? Model { get; set; }
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(1.5);
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan FailureBackoff { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed class LmStudioClient : IDisposable
{
    private readonly TimeProvider _time;
    private readonly HttpClient _http;
    private long _backoffUntilTicks;

    public LmStudioClient(LmStudioOptions options, TimeProvider time, HttpMessageHandler? handler = null)
    {
        Options = options;
        _time = time;
        handler ??= new SocketsHttpHandler { ConnectTimeout = options.ConnectTimeout };
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public LmStudioOptions Options { get; }

    public bool IsBackingOff => _time.GetUtcNow().UtcTicks < Interlocked.Read(ref _backoffUntilTicks);

    public async Task<ParsedIntent?> TryParseAsync(string text, DateTimeOffset now, ParsedIntent? pending, CancellationToken ct = default)
    {
        if (!Options.Enabled || IsBackingOff) return null;
        try
        {
            using var timeout = new CancellationTokenSource(Options.RequestTimeout, _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var baseUrl = Options.BaseUrl.AbsoluteUri;
            if (!baseUrl.EndsWith('/')) baseUrl += "/";
            using var content = new StringContent(BuildRequest(text, now, pending, Options.Model).ToJsonString(), System.Text.Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(new Uri(baseUrl + "chat/completions"), content, linked.Token).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            var intent = ParseResponse(body, now);
            if (intent is null) Fail();
            return intent;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { Fail(); return null; }
    }

    private void Fail() =>
        Interlocked.Exchange(ref _backoffUntilTicks, (_time.GetUtcNow() + Options.FailureBackoff).UtcTicks);

    internal static JsonObject BuildRequest(string text, DateTimeOffset now, ParsedIntent? pending, string? model)
    {
        var off = now.Offset;
        var offText = (off < TimeSpan.Zero ? "-" : "+") + off.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        var system =
            "You turn chat messages for an alarm and reminder assistant into one JSON intent. " +
            $"Current local date/time: {now.ToString("dddd yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} (UTC{offText}). " +
            "Return times as local \"yyyy-MM-ddTHH:mm\" in the future; use null when no time is given. " +
            "kind is set_alarm for alarms and wake-ups, set_reminder for reminders and tasks, list, cancel (cancel_query = what to cancel), help, greeting or unknown. " +
            "label is the short task without time words, or null.";
        var user = text;
        if (pending is { NeedsTime: true })
            user += $"\n(Pending request waiting for a time: {ToWire(pending.Kind)}, label: {pending.Label ?? "none"})";

        JsonObject NullableString() => new() { ["type"] = new JsonArray("string", "null") };
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["kind"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray("set_alarm", "set_reminder", "list", "cancel", "help", "greeting", "unknown"),
                },
                ["when_local"] = NullableString(),
                ["label"] = NullableString(),
                ["cancel_query"] = NullableString(),
            },
            ["required"] = new JsonArray("kind", "when_local", "label", "cancel_query"),
            ["additionalProperties"] = false,
        };
        var req = new JsonObject();
        if (!string.IsNullOrWhiteSpace(model)) req["model"] = model;
        req["temperature"] = 0;
        req["messages"] = new JsonArray(
            new JsonObject { ["role"] = "system", ["content"] = system },
            new JsonObject { ["role"] = "user", ["content"] = user });
        req["response_format"] = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject { ["name"] = "aisa_intent", ["strict"] = true, ["schema"] = schema },
        };
        return req;
    }

    private static string ToWire(IntentKind k) => k switch
    {
        IntentKind.SetAlarm => "set_alarm",
        IntentKind.SetReminder => "set_reminder",
        _ => "unknown",
    };

    internal static ParsedIntent? ParseResponse(string body, DateTimeOffset now)
    {
        try
        {
            using var outer = JsonDocument.Parse(body);
            var content = outer.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content)) return null;
            content = content.Trim();
            // Some models wrap JSON in a markdown fence despite the schema.
            var a = content.IndexOf('{'); var b = content.LastIndexOf('}');
            if (a < 0 || b < a) return null;
            content = content[a..(b + 1)];
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            string? Str(string name) =>
                root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString())
                    ? p.GetString()!.Trim() : null;

            var kind = Str("kind")?.ToLowerInvariant() switch
            {
                "set_alarm" => IntentKind.SetAlarm,
                "set_reminder" => IntentKind.SetReminder,
                "list" => IntentKind.List,
                "cancel" => IntentKind.Cancel,
                "help" => IntentKind.Help,
                "greeting" => IntentKind.Greeting,
                "unknown" => IntentKind.Unknown,
                _ => (IntentKind?)null,
            };
            if (kind is null) return null;

            DateTimeOffset? when = null;
            if (Str("when_local") is { } w &&
                DateTime.TryParseExact(w, ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            {
                var t = new DateTimeOffset(local, now.Offset);
                if (t > now) when = t;
            }

            var label = Str("label");
            if (label is not null) label = char.ToUpperInvariant(label[0]) + label[1..];
            if (kind == IntentKind.SetAlarm) label ??= "Alarm";
            var isSet = kind is IntentKind.SetAlarm or IntentKind.SetReminder;
            return new ParsedIntent(kind.Value, isSet ? when : null, isSet ? label : null,
                kind == IntentKind.Cancel ? Str("cancel_query") : null);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
