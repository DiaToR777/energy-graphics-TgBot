using GrafikSvitlaBot.Model;
using GrafikSvitlaBot.Services.Cache;

namespace GrafikSvitlaBot.Services;

public class GraphicsService
{
    private readonly GrafikCache _cache = new();

    private readonly HttpClient _http;
    private readonly string _baseUrl = "https://raw.githubusercontent.com/DiaToR777/energy-graphics-bot/main/Parser/graphics/";

    private readonly SemaphoreSlim _updateCheckLock = new(1, 1);

    private string? _lastKnownUpdateTag = null;

    private DateTime _lastCheckTime = DateTime.MinValue;
    public GraphicsService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<GrafikData>> GetGraphicsAsync(CancellationToken ct)
    {
        await _updateCheckLock.WaitAsync(ct);
        try
        {
            if (!_cache.IsEmpty() && (DateTime.UtcNow - _lastCheckTime).TotalMinutes < 60)
            {
                return _cache.Get();
            }

            // 2. Якщо час вийшов — ліземо на GitHub за тегом
            string currentTagFromServer;
            try
            {
                currentTagFromServer = (await _http.GetStringAsync(_baseUrl + "last_update.txt", ct)).Trim();
                _lastCheckTime = DateTime.UtcNow; // Оновлюємо час ПЕРЕВІРКИ
            }
            catch
            {
                currentTagFromServer = "error";
            }
            if (_lastKnownUpdateTag == currentTagFromServer && !_cache.IsEmpty())
                return _cache.Get();

            var newImages = await DownloadImages(ct);
            if (newImages.Any())
            {
                await _cache.Update(newImages, ct);
                _lastKnownUpdateTag = currentTagFromServer;
            }

            return _cache.Get();
        }
        finally { _updateCheckLock.Release(); }
    }

    public async Task<List<GrafikData>> DownloadImages(CancellationToken ct)
    {
        var result = new List<GrafikData>();

        for (int i = 1; i <= 3; i++)
        {
            try
            {
                var name = $"grafic_{i}.png";
                var bytes = await _http.GetByteArrayAsync(_baseUrl + name, ct);
                result.Add(new GrafikData
                {
                    Bytes = bytes,
                    FileName = name
                });
            }
            catch (HttpRequestException)
            {
                break;
            }
        }
        return result;
    }
    public string GetCurrentUpdateText() => _lastKnownUpdateTag ?? "невідомо";
}
