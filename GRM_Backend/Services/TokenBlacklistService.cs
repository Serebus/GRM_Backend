using GRM_Backend.Data;
using GRM_Backend.Models;

namespace GRM_Backend.Services;

public class TokenBlacklistService : ITokenBlacklistService
{
    private readonly AppDbContext _context;

    public TokenBlacklistService(AppDbContext context)
    {
        _context = context;
    }

    public void BlacklistToken(string token, DateTime expiryDate)
    {
        if (string.IsNullOrWhiteSpace(token)) return;

        var existing = _context.RevokedTokens.Any(t => t.Token == token);
        if (!existing)
        {
            _context.RevokedTokens.Add(new RevokedToken
            {
                Token = token,
                ExpiryDate = expiryDate,
                RevokedAt = DateTime.UtcNow
            });
            _context.SaveChanges();
        }
    }

    public bool IsTokenBlacklisted(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        return _context.RevokedTokens.Any(t => t.Token == token && t.ExpiryDate > DateTime.UtcNow);
    }
}
