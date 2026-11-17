namespace Aisa.Nlu;

public sealed class NluPipeline(LmStudioClient? llm)
{
    public async Task<NluResult> ParseAsync(string text, DateTimeOffset now, ParsedIntent? pending = null, CancellationToken ct = default)
    {
        var o = DeterministicParser.Parse(text, now, pending);
        var i = o.Intent;
        var wantLlm = i.Kind == IntentKind.Unknown || (i.NeedsTime && o.LooksTimed) || o.TimeWordsInLabel;
        if (!wantLlm || llm is null) return new(i, false);
        var viaLlm = await llm.TryParseAsync(text, now, pending, ct).ConfigureAwait(false);
        return viaLlm is null ? new(i, false) : new(viaLlm, true);
    }
}
