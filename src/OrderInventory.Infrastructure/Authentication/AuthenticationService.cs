using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using OrderInventory.Application.DTOs.Auth;
using OrderInventory.Application.Exceptions;
using OrderInventory.Application.Interfaces;
using OrderInventory.Domain.Entities;
using OrderInventory.Domain.Enums;
using OrderInventory.Infrastructure.Persistence;

namespace OrderInventory.Infrastructure.Authentication
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthenticationService> _logger;

        public AuthenticationService(
            ApplicationDbContext context,
            IPasswordHasher<User> passwordHasher,
            IConfiguration configuration,
            ILogger<AuthenticationService> logger)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<RegisterResponseDto> RegisterAsync(
            RegisterRequestDto request)
        {
            var email = NormalizeEmail(request.Email);

            var exists = await _context.Users
                .AnyAsync(x => x.Email == email);

            if (exists)
            {
                throw new ApiException(
                    409,
                    "An account with this email already exists.");
            }

            var user = new User
            {
                FullName = request.FullName.Trim(),
                Email = email,
                Role = UserRole.Customer
            };

            user.PasswordHash = _passwordHasher.HashPassword(
                user,
                request.Password);

            _context.Users.Add(user);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // The unique database index is the final protection
                // against simultaneous registrations.
                if (await _context.Users.AnyAsync(x => x.Email == email))
                {
                    throw new ApiException(
                        409,
                        "An account with this email already exists.");
                }

                throw;
            }

            _logger.LogInformation(
                "Customer registered successfully. UserId: {UserId}",
                user.Id);

            return new RegisterResponseDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role.ToString()
            };
        }

        public async Task<AuthResponseDto> LoginAsync(
            LoginRequestDto request)
        {
            var email = NormalizeEmail(request.Email);

            var user = await _context.Users
                .SingleOrDefaultAsync(x => x.Email == email);

            if (user == null)
            {
                throw new ApiException(
                    401,
                    "Invalid email or password.");
            }

            var verificationResult = _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.Password);

            if (verificationResult == PasswordVerificationResult.Failed)
            {
                throw new ApiException(
                    401,
                    "Invalid email or password.");
            }

            if (verificationResult ==
                PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _passwordHasher.HashPassword(
                    user,
                    request.Password);

                await _context.SaveChangesAsync();
            }

            return await CreateAuthResponseAsync(user);
        }

        public async Task<AuthResponseDto> RefreshAsync(
            RefreshTokenRequestDto request)
        {
            var incomingHash = HashToken(request.RefreshToken);

            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            var storedToken = await _context.RefreshTokens
                .Include(x => x.User)
                .SingleOrDefaultAsync(x =>
                    x.TokenHash == incomingHash);

            var now = DateTime.UtcNow;

            if (storedToken == null ||
                storedToken.RevokedAt != null ||
                storedToken.ExpiresAt <= now)
            {
                throw new ApiException(
                    401,
                    "Invalid or expired refresh token.");
            }

            // Generate a replacement refresh token.
            var newRefreshToken = GenerateRefreshToken();

            var replacement = new RefreshToken
            {
                UserId = storedToken.UserId,
                TokenHash = HashToken(newRefreshToken),
                ExpiresAt = now.AddDays(GetRefreshExpirationDays()),
                CreatedAt = now
            };

            _context.RefreshTokens.Add(replacement);

            await _context.SaveChangesAsync();

            // Atomically revoke the old token.
            // This prevents two concurrent requests from rotating
            // the same refresh token successfully.
            var affectedRows = await _context.RefreshTokens
                .Where(x =>
                    x.Id == storedToken.Id &&
                    x.RevokedAt == null &&
                    x.ExpiresAt > now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.RevokedAt, now)
                    .SetProperty(
                        x => x.ReplacedByTokenId,
                        replacement.Id));

            if (affectedRows != 1)
            {
                await transaction.RollbackAsync();

                throw new ApiException(
                    401,
                    "Refresh token has already been used or revoked.");
            }

            var response = CreateResponse(
                storedToken.User,
                newRefreshToken,
                replacement.ExpiresAt);

            await transaction.CommitAsync();

            _logger.LogInformation(
                "Refresh token rotated for UserId: {UserId}",
                storedToken.UserId);

            return response;
        }

        private async Task<AuthResponseDto> CreateAuthResponseAsync(
            User user)
        {
            var refreshToken = GenerateRefreshToken();

            var refreshExpiration = DateTime.UtcNow.AddDays(
                GetRefreshExpirationDays());

            var tokenEntity = new RefreshToken
            {
                UserId = user.Id,
                TokenHash = HashToken(refreshToken),
                ExpiresAt = refreshExpiration
            };

            _context.RefreshTokens.Add(tokenEntity);

            await _context.SaveChangesAsync();

            return CreateResponse(
                user,
                refreshToken,
                refreshExpiration);
        }

        private AuthResponseDto CreateResponse(
            User user,
            string refreshToken,
            DateTime refreshExpiration)
        {
            var accessExpiration = DateTime.UtcNow.AddMinutes(
                GetAccessExpirationMinutes());

            return new AuthResponseDto
            {
                AccessToken = GenerateAccessToken(
                    user,
                    accessExpiration),

                RefreshToken = refreshToken,

                AccessTokenExpiresAt = accessExpiration,

                RefreshTokenExpiresAt = refreshExpiration,

                UserId = user.Id,

                FullName = user.FullName,

                Email = user.Email,

                Role = user.Role.ToString()
            };
        }

        private string GenerateAccessToken(
            User user,
            DateTime expiration)
        {
            var secretKey = GetRequiredConfiguration(
                "JwtSettings:SecretKey");

            var issuer = GetRequiredConfiguration(
                "JwtSettings:Issuer");

            var audience = GetRequiredConfiguration(
                "JwtSettings:Audience");

            var key = new SymmetricSecurityKey(
                Convert.FromBase64String(secretKey));

            var credentials = new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
            new Claim(
                JwtRegisteredClaimNames.Sub,
                user.Id.ToString()),

            new Claim(
                JwtRegisteredClaimNames.Email,
                user.Email),

            new Claim(
                JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString()),

            new Claim(
                ClaimTypes.NameIdentifier,
                user.Id.ToString()),

            new Claim(
                ClaimTypes.Name,
                user.FullName),

            new Claim(
                ClaimTypes.Role,
                user.Role.ToString())
        };

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: expiration,
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler()
                .WriteToken(token);
        }

        private string GenerateRefreshToken()
        {
            var randomBytes = RandomNumberGenerator.GetBytes(64);

            return Convert.ToBase64String(randomBytes);
        }

        private static string HashToken(string token)
        {
            var bytes = SHA256.HashData(
                Encoding.UTF8.GetBytes(token));

            return Convert.ToHexString(bytes);
        }

        private string NormalizeEmail(string email)
        {
            return email.Trim().ToLowerInvariant();
        }

        private int GetAccessExpirationMinutes()
        {
            return _configuration.GetValue<int>(
                "JwtSettings:AccessTokenExpirationMinutes", 15);
        }

        private int GetRefreshExpirationDays()
        {
            return _configuration.GetValue<int>(
                "JwtSettings:RefreshTokenExpirationDays", 7);
        }

        private string GetRequiredConfiguration(string key)
        {
            return _configuration[key]
                ?? throw new InvalidOperationException(
                    $"Missing configuration: {key}");
        }
    }
}
