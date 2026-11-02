using GRM_Backend.Models;

namespace GRM_Backend.Services;

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) GenerateToken(User user);
}
