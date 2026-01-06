using System.Collections.Concurrent;

namespace GrafikSvitlaBot.Services;

public class RateLimiter
{
    private readonly ConcurrentDictionary<long, DateTime> _lastRequestTimes = new();
    private readonly int _rateLimitSeconds;

    public RateLimiter(int rate_limit_seconds )
    {
        _rateLimitSeconds = rate_limit_seconds;
    }

    public (bool IsAllowed, int RemainingSeconds) CheckRateLimit(long chatId)
    {
        var currentTime = DateTime.UtcNow;

        if (_lastRequestTimes.TryGetValue(chatId, out var lastTime))
        {
            var elapsed = (currentTime - lastTime).TotalSeconds;
            var remaining = _rateLimitSeconds - (int)elapsed;

            if (remaining > 0)
                return (false, remaining);  
        }

        _lastRequestTimes[chatId] = currentTime;
        return (true, 0);  
    }
}

