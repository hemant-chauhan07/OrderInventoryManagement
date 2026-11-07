using OrderInventory.Application.DTOs.Auth;

namespace OrderInventory.Application.Interfaces
{
    public interface IAuthenticationService
    {
        Task<RegisterResponseDto> RegisterAsync(
            RegisterRequestDto request);

        Task<AuthResponseDto> LoginAsync(
            LoginRequestDto request);

        Task<AuthResponseDto> RefreshAsync(
            RefreshTokenRequestDto request);
    }
}
