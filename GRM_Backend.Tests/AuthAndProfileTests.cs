using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GRM_Backend.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GRM_Backend.Tests;

public class AuthAndProfileTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthAndProfileTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_WithValidData_ReturnsCreated()
    {
        var client = _factory.CreateClient();
        var username = $"user_{Guid.NewGuid():N}";
        var registerRequest = new RegisterRequest
        {
            Username = username,
            Email = $"{username}@example.com",
            Password = "Password123!",
            FullName = "Jane Doe",
            PhoneNumber = "1234567890",
            Address = "123 Main Street"
        };

        var response = await client.PostAsJsonAsync("/api/auth/register", registerRequest);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(user);
        Assert.Equal(username, user.Username);
        Assert.Equal($"{username}@example.com", user.Email);
        Assert.Equal("Customer", user.Role);
        Assert.Equal("Jane Doe", user.FullName);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var username = $"dup_{Guid.NewGuid():N}";
        var registerRequest = new RegisterRequest
        {
            Username = username,
            Email = $"{username}@example.com",
            Password = "Password123!"
        };

        var firstResponse = await client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var secondRequest = new RegisterRequest
        {
            Username = $"other_{Guid.NewGuid():N}",
            Email = $"{username}@example.com",
            Password = "Password123!"
        };
        var secondResponse = await client.PostAsJsonAsync("/api/auth/register", secondRequest);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsJwtToken()
    {
        var client = _factory.CreateClient();
        var username = $"login_{Guid.NewGuid():N}";
        var registerRequest = new RegisterRequest
        {
            Username = username,
            Email = $"{username}@example.com",
            Password = "SecurePassword123!"
        };
        await client.PostAsJsonAsync("/api/auth/register", registerRequest);

        var loginRequest = new LoginRequest
        {
            EmailOrUsername = $"{username}@example.com",
            Password = "SecurePassword123!"
        };

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", loginRequest);

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var authResult = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(authResult);
        Assert.False(string.IsNullOrWhiteSpace(authResult.Token));
        Assert.Equal("Bearer", authResult.TokenType);
        Assert.Equal("Customer", authResult.User.Role);
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var username = $"invalidpwd_{Guid.NewGuid():N}";
        var registerRequest = new RegisterRequest
        {
            Username = username,
            Email = $"{username}@example.com",
            Password = "SecurePassword123!"
        };
        await client.PostAsJsonAsync("/api/auth/register", registerRequest);

        var loginRequest = new LoginRequest
        {
            EmailOrUsername = $"{username}@example.com",
            Password = "WrongPassword!"
        };

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", loginRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }

    [Fact]
    public async Task GetMe_WithoutAuth_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_WithValidAuth_ReturnsCurrentUserAndRole()
    {
        var client = _factory.CreateClient();
        var username = $"me_{Guid.NewGuid():N}";
        var registerRequest = new RegisterRequest
        {
            Username = username,
            Email = $"{username}@example.com",
            Password = "Password123!"
        };
        await client.PostAsJsonAsync("/api/auth/register", registerRequest);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            EmailOrUsername = username,
            Password = "Password123!"
        });
        var authResult = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult!.Token);
        var meResponse = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(me);
        Assert.Equal(username, me.Username);
        Assert.Equal("Customer", me.Role);
    }

    [Fact]
    public async Task Logout_EndsSession_RevokingSubsequentRequests()
    {
        var client = _factory.CreateClient();
        var username = $"logout_{Guid.NewGuid():N}";
        var registerRequest = new RegisterRequest
        {
            Username = username,
            Email = $"{username}@example.com",
            Password = "Password123!"
        };
        await client.PostAsJsonAsync("/api/auth/register", registerRequest);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            EmailOrUsername = username,
            Password = "Password123!"
        });
        var authResult = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult!.Token);

        // First call to /api/auth/me succeeds
        var beforeLogout = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, beforeLogout.StatusCode);

        // Call logout
        var logoutResponse = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        // Second call with same token fails due to token blacklist
        var afterLogout = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task Profile_GetAndPut_SuccessfullyViewsAndUpdatesProfile()
    {
        var client = _factory.CreateClient();
        var username = $"profile_{Guid.NewGuid():N}";
        var registerRequest = new RegisterRequest
        {
            Username = username,
            Email = $"{username}@example.com",
            Password = "Password123!",
            FullName = "Original Name",
            PhoneNumber = "000-0000",
            Address = "Original Address"
        };
        await client.PostAsJsonAsync("/api/auth/register", registerRequest);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            EmailOrUsername = username,
            Password = "Password123!"
        });
        var authResult = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResult!.Token);

        // GET /api/profile
        var getProfileResponse = await client.GetAsync("/api/profile");
        Assert.Equal(HttpStatusCode.OK, getProfileResponse.StatusCode);
        var profile = await getProfileResponse.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.NotNull(profile);
        Assert.Equal("Original Name", profile.FullName);
        Assert.Equal("Original Address", profile.Address);
        Assert.Equal("Customer", profile.Role);

        // PUT /api/profile
        var updateRequest = new UpdateProfileRequest
        {
            FullName = "Updated Name",
            PhoneNumber = "111-2222",
            Address = "New Avenue 456"
        };
        var updateResponse = await client.PutAsJsonAsync("/api/profile", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updatedProfile = await updateResponse.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.NotNull(updatedProfile);
        Assert.Equal("Updated Name", updatedProfile.FullName);
        Assert.Equal("111-2222", updatedProfile.PhoneNumber);
        Assert.Equal("New Avenue 456", updatedProfile.Address);
        Assert.NotNull(updatedProfile.UpdatedAt);

        // GET again to verify persistence
        var getUpdatedProfile = await client.GetAsync("/api/profile");
        Assert.Equal(HttpStatusCode.OK, getUpdatedProfile.StatusCode);
        var fetchedUpdated = await getUpdatedProfile.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.NotNull(fetchedUpdated);
        Assert.Equal("Updated Name", fetchedUpdated.FullName);
    }

    [Fact]
    public async Task Profile_WithNonCustomerRole_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        
        var user = new GRM_Backend.Models.User
        {
            Id = 9999,
            Username = "admin_user",
            Email = "admin@example.com",
            Role = "Admin"
        };
        using var scope = _factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<GRM_Backend.Services.ITokenService>();
        var (token, _) = tokenService.GenerateToken(user);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/profile");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
