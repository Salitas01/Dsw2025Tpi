

namespace Dsw2025Tpi.Application.Dtos
{
    public record RegisterModel(string Username, string Password, string Email);

    public record RegisterAuthModel(string Username, string Password, string Email, string Role);

}
