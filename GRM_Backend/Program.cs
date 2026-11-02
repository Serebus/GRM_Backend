using System.Text;
using GRM_Backend.Data;
using GRM_Backend.Models;
using GRM_Backend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add Database Context (In-Memory Database for reliable operation and testing)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseInMemoryDatabase("GRM_Backend_Db"));

// Add Services
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddSingleton<ITokenBlacklistService, TokenBlacklistService>();

// Add JWT Authentication
var jwtSecret = builder.Configuration["JwtSettings:Secret"] ?? "SuperSecretKeyForJwtTokenGenerationAndValidationInDevelopmentAndProduction123456!";
var jwtIssuer = builder.Configuration["JwtSettings:Issuer"] ?? "GRM_Backend";
var jwtAudience = builder.Configuration["JwtSettings:Audience"] ?? "GRM_Backend";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

    options.Events = new JwtBearerEvents
    {
        // Accept both "Bearer <token>" and a raw "<token>" in the Authorization header
        OnMessageReceived = context =>
        {
            var authHeader = context.Request.Headers.Authorization.ToString().Trim();
            if (!string.IsNullOrEmpty(authHeader))
            {
                context.Token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? authHeader.Substring("Bearer ".Length).Trim().Trim('"')
                    : authHeader.Trim('"');
            }
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            var blacklistService = context.HttpContext.RequestServices.GetRequiredService<ITokenBlacklistService>();
            var token = (context.SecurityToken as Microsoft.IdentityModel.JsonWebTokens.JsonWebToken)?.EncodedToken
                        ?? (context.SecurityToken as System.IdentityModel.Tokens.Jwt.JwtSecurityToken)?.RawData;
            if (token != null && blacklistService.IsTokenBlacklisted(token))
            {
                context.Fail("This token has been revoked.");
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Configure Swagger/OpenAPI with JWT Bearer support
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "GRM Backend API",
        Version = "v1",
        Description = "API for Authentication and User Profile Management"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Enter: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Seed a default admin account (change the password in production)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
    if (!db.Users.Any(u => u.Role == "Admin"))
    {
        var admin = new User { Username = "admin", Email = "admin@grm.local", Role = "Admin", FullName = "Administrator" };
        admin.PasswordHash = hasher.HashPassword(admin, builder.Configuration["AdminSeed:Password"] ?? "Admin123!");
        db.Users.Add(admin);
        db.SaveChanges();
    }
}

// Configure HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseSwagger(c => c.SerializeAsV2 = true);
// Alias so tools expecting /openapi/v1.json also get a valid OpenAPI document
app.UseSwagger(c =>
{
    c.RouteTemplate = "openapi/{documentName}.json";
    c.SerializeAsV2 = true;
});
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "GRM Backend API v1");
    c.RoutePrefix = string.Empty; // Serve Swagger at root for convenience
});

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Expose Program for WebApplicationFactory tests
public partial class Program { }
