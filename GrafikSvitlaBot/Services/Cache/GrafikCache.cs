using GrafikSvitlaBot.Model;

namespace GrafikSvitlaBot.Services.Cache;

public class GrafikCache
{
    private List<GrafikData> _cache = new();
    private DateTime _lastUpdate = DateTime.MinValue;
    private readonly TimeSpan _ttl = TimeSpan.FromHours(1);
    SemaphoreSlim updateLock = new(1,1);

    public bool IsExpired()
        => DateTime.UtcNow - _lastUpdate > _ttl;

    public List<GrafikData> Get()
        => _cache;
    public bool IsEmpty() => _cache == null || !_cache.Any();

    public async Task Update(List<GrafikData> data, CancellationToken ct)
    {
        await updateLock.WaitAsync(ct);
        try
        {
            _cache = data;
            _lastUpdate = DateTime.UtcNow;
        }
        finally
        {
            updateLock.Release();
        }
    }

    public DateTime GetLastUpdateTime()
        => _lastUpdate;
}
