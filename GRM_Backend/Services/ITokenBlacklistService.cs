namespace GRM_Backend.Services;

public interface ITokenBlacklistService
{
    void BlacklistToken(string token, DateTime expiryDate);
    bool IsTokenBlacklisted(string token);
}
