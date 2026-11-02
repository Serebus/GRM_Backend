using System.Collections.Concurrent;

namespace GRM_Backend.Services;

public class TokenBlacklistService : ITokenBlacklistService
{
    private readonly ConcurrentDictionary<string, DateTime> _blacklistedTokens = new();

    public void BlacklistToken(string token, DateTime expiryDate)
    {
        CleanupExpired();
        _blacklistedTokens.TryAdd(token, expiryDate);
    }

    public bool IsTokenBlacklisted(string token)
    {
        CleanupExpired();
        return _blacklistedTokens.ContainsKey(token);
    }

    private void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in _blacklistedTokens)
        {
            if (pair.Value < now)
            {
                _blacklistedTokens.TryRemove(pair.Key, out _);
            }
        }
    }
}
