using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GRM_Backend.Data;
using GRM_Backend.DTOs;
using GRM_Backend.Models;
using GRM_Backend.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GRM_Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly ITokenBlacklistService _blacklistService;
    private readonly IPasswordHasher<User> _passwordHasher;

    public AuthController(
        AppDbContext context,
        ITokenService tokenService,
        ITokenBlacklistService blacklistService,
        IPasswordHasher<User> passwordHasher)
    {
        _context = context;
        _tokenService = tokenService;
        _blacklistService = blacklistService;
        _passwordHasher = passwordHasher;
    }

    private string? GetIpAddress()
    {
        if (Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            return forwarded.FirstOrDefault();
        }
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    private void SetRefreshTokenCookie(string token, DateTime expires)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Expires = expires,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax
        };
        Response.Cookies.Append("refreshToken", token, cookieOptions);
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var normalizedUsername = request.Username.Trim();

        var existingUser = await _context.Users
            .AnyAsync(u => u.Email.ToLower() == normalizedEmail || u.Username.ToLower() == normalizedUsername.ToLower());

        if (existingUser)
        {
            return Conflict(new MessageResponse { Message = "A user with the specified email or username already exists." });
        }

        var user = new User
        {
            Username = normalizedUsername,
            Email = normalizedEmail,
            Role = "Customer",
            FullName = request.FullName?.Trim(),
            PhoneNumber = request.PhoneNumber?.Trim(),
            Address = request.Address?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var userDto = new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Role = user.Role,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            Address = user.Address,
            CreatedAt = user.CreatedAt
        };

        return CreatedAtAction(nameof(Me), userDto);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var identifier = request.EmailOrUsername.Trim().ToLowerInvariant();
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == identifier || u.Username.ToLower() == identifier);

        if (user == null)
        {
            return Unauthorized(new MessageResponse { Message = "Invalid credentials." });
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new MessageResponse { Message = "Invalid credentials." });
        }

        var (jwtToken, jwtExpiresAt) = _tokenService.GenerateToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken(GetIpAddress());
        refreshToken.UserId = user.Id;

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        SetRefreshTokenCookie(refreshToken.Token, refreshToken.ExpiresAt);

        var response = new AuthResponse
        {
            Token = jwtToken,
            TokenType = "Bearer",
            ExpiresAt = jwtExpiresAt,
            RefreshToken = refreshToken.Token,
            RefreshTokenExpiresAt = refreshToken.ExpiresAt,
            User = new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role,
                FullName = user.FullName,
                PhoneNumber = user.PhoneNumber,
                Address = user.Address,
                CreatedAt = user.CreatedAt
            }
        };

        return Ok(response);
    }

    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest? request)
    {
        var token = request?.RefreshToken ?? Request.Cookies["refreshToken"];
        if (string.IsNullOrWhiteSpace(token))
        {
            return BadRequest(new MessageResponse { Message = "Refresh token is required." });
        }

        var refreshToken = await _context.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == token);

        if (refreshToken == null)
        {
            return Unauthorized(new MessageResponse { Message = "Invalid refresh token." });
        }

        // Security check: Detection of compromised/reused refresh token
        if (refreshToken.IsRevoked)
        {
            var userTokens = await _context.RefreshTokens
                .Where(r => r.UserId == refreshToken.UserId && r.RevokedAt == null)
                .ToListAsync();

            var ip = GetIpAddress();
            foreach (var t in userTokens)
            {
                t.RevokedAt = DateTime.UtcNow;
                t.RevokedByIp = ip;
                t.ReasonRevoked = $"Attempted reuse of revoked token {refreshToken.Token}";
            }

            await _context.SaveChangesAsync();
            return Unauthorized(new MessageResponse { Message = "Invalid or revoked refresh token. All active sessions have been revoked for security." });
        }

        if (refreshToken.IsExpired)
        {
            return Unauthorized(new MessageResponse { Message = "Refresh token has expired." });
        }

        var user = refreshToken.User;
        if (user == null)
        {
            return Unauthorized(new MessageResponse { Message = "User not found." });
        }

        // Rotate refresh token
        var (newJwtToken, newJwtExpiresAt) = _tokenService.GenerateToken(user);
        var newRefreshToken = _tokenService.GenerateRefreshToken(GetIpAddress());
        newRefreshToken.UserId = user.Id;

        refreshToken.RevokedAt = DateTime.UtcNow;
        refreshToken.RevokedByIp = GetIpAddress();
        refreshToken.ReplacedByToken = newRefreshToken.Token;
        refreshToken.ReasonRevoked = "Replaced by new token";

        _context.RefreshTokens.Add(newRefreshToken);
        await _context.SaveChangesAsync();

        SetRefreshTokenCookie(newRefreshToken.Token, newRefreshToken.ExpiresAt);

        var response = new AuthResponse
        {
            Token = newJwtToken,
            TokenType = "Bearer",
            ExpiresAt = newJwtExpiresAt,
            RefreshToken = newRefreshToken.Token,
            RefreshTokenExpiresAt = newRefreshToken.ExpiresAt,
            User = new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role,
                FullName = user.FullName,
                PhoneNumber = user.PhoneNumber,
                Address = user.Address,
                CreatedAt = user.CreatedAt
            }
        };

        return Ok(response);
    }

    [HttpPost("revoke-token")]
    [Authorize]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeToken([FromBody] RevokeTokenRequest? request)
    {
        var token = request?.RefreshToken ?? Request.Cookies["refreshToken"];
        if (string.IsNullOrWhiteSpace(token))
        {
            return BadRequest(new MessageResponse { Message = "Token is required." });
        }

        var refreshToken = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.Token == token);
        if (refreshToken == null)
        {
            return NotFound(new MessageResponse { Message = "Token not found." });
        }

        // Only allow owners or Admins to revoke
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
        if (userRole != "Admin" && (!int.TryParse(userIdClaim, out var currentUserId) || refreshToken.UserId != currentUserId))
        {
            return Forbid();
        }

        if (refreshToken.IsActive)
        {
            refreshToken.RevokedAt = DateTime.UtcNow;
            refreshToken.RevokedByIp = GetIpAddress();
            refreshToken.ReasonRevoked = "Revoked by user";
            await _context.SaveChangesAsync();
        }

        return Ok(new MessageResponse { Message = "Token revoked successfully." });
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout([FromBody] RevokeTokenRequest? request)
    {
        // 1. Blacklist the JWT access token in DB
        var token = await HttpContext.GetTokenAsync("access_token");
        if (!string.IsNullOrEmpty(token))
        {
            var handler = new JwtSecurityTokenHandler();
            if (handler.CanReadToken(token))
            {
                var jwtToken = handler.ReadJwtToken(token);
                var expiryDate = jwtToken.ValidTo > DateTime.MinValue ? jwtToken.ValidTo : DateTime.UtcNow.AddHours(2);
                _blacklistService.BlacklistToken(token, expiryDate);
            }
        }

        // 2. Revoke the refresh token (from body, cookie, or most recent for user)
        var refreshTokenString = request?.RefreshToken ?? Request.Cookies["refreshToken"];
        if (!string.IsNullOrWhiteSpace(refreshTokenString))
        {
            var rt = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.Token == refreshTokenString);
            if (rt != null && rt.IsActive)
            {
                rt.RevokedAt = DateTime.UtcNow;
                rt.RevokedByIp = GetIpAddress();
                rt.ReasonRevoked = "Logged out";
            }
        }
        else
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var userId))
            {
                var latestRt = await _context.RefreshTokens
                    .Where(r => r.UserId == userId && r.RevokedAt == null && r.ExpiresAt > DateTime.UtcNow)
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefaultAsync();

                if (latestRt != null)
                {
                    latestRt.RevokedAt = DateTime.UtcNow;
                    latestRt.RevokedByIp = GetIpAddress();
                    latestRt.ReasonRevoked = "Logged out";
                }
            }
        }

        await _context.SaveChangesAsync();
        Response.Cookies.Delete("refreshToken");

        return Ok(new MessageResponse { Message = "Logged out successfully." });
    }

    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LogoutAll()
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (!string.IsNullOrEmpty(token))
        {
            var handler = new JwtSecurityTokenHandler();
            if (handler.CanReadToken(token))
            {
                var jwtToken = handler.ReadJwtToken(token);
                var expiryDate = jwtToken.ValidTo > DateTime.MinValue ? jwtToken.ValidTo : DateTime.UtcNow.AddHours(2);
                _blacklistService.BlacklistToken(token, expiryDate);
            }
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(userIdClaim, out var userId))
        {
            var activeTokens = await _context.RefreshTokens
                .Where(r => r.UserId == userId && r.RevokedAt == null && r.ExpiresAt > DateTime.UtcNow)
                .ToListAsync();

            var ip = GetIpAddress();
            foreach (var rt in activeTokens)
            {
                rt.RevokedAt = DateTime.UtcNow;
                rt.RevokedByIp = ip;
                rt.ReasonRevoked = "Logged out of all sessions";
            }

            await _context.SaveChangesAsync();
        }

        Response.Cookies.Delete("refreshToken");
        return Ok(new MessageResponse { Message = "Logged out of all sessions successfully." });
    }

    [HttpGet("sessions")]
    [Authorize]
    [ProducesResponseType(typeof(IEnumerable<SessionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSessions()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var sessions = await _context.RefreshTokens
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(10)
            .Select(r => new SessionDto
            {
                Id = r.Id,
                CreatedAt = r.CreatedAt,
                ExpiresAt = r.ExpiresAt,
                CreatedByIp = r.CreatedByIp,
                IsActive = r.RevokedAt == null && r.ExpiresAt > DateTime.UtcNow
            })
            .ToListAsync();

        return Ok(sessions);
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Me()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new MessageResponse { Message = "Invalid user identification in token." });
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFound(new MessageResponse { Message = "User not found." });
        }

        var userDto = new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Role = user.Role,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            Address = user.Address,
            CreatedAt = user.CreatedAt
        };

        return Ok(userDto);
    }
}
