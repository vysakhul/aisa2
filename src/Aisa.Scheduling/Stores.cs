using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aisa.Scheduling;

public interface IScheduleStore
{
    Task<IReadOnlyList<ScheduledItem>> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(IReadOnlyList<ScheduledItem> items, CancellationToken ct = default);
}

public sealed class InMemoryScheduleStore : IScheduleStore
{
    private readonly object _gate = new();
    private List<ScheduledItem> _items = [];

    public int SaveCount { get; private set; }

    public Task<IReadOnlyList<ScheduledItem>> LoadAsync(CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<ScheduledItem>>(_items.ToList());
    }

    public Task SaveAsync(IReadOnlyList<ScheduledItem> items, CancellationToken ct = default)
    {
        lock (_gate) { _items = items.ToList(); SaveCount++; }
        return Task.CompletedTask;
    }
}

public sealed class JsonScheduleStore(string path) : IScheduleStore
{
    private sealed record Envelope(int Version, List<ScheduledItem> Items);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<IReadOnlyList<ScheduledItem>> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(path)) return [];
        try
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var env = await JsonSerializer.DeserializeAsync<Envelope>(fs, Json, ct);
            if (env?.Items is null) throw new JsonException("missing items");
            return env.Items;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            try { File.Move(path, $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}", overwrite: true); }
            catch { }
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyList<ScheduledItem> items, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(fs, new Envelope(1, items.ToList()), Json, ct);
                await fs.FlushAsync(ct);
                fs.Flush(true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
