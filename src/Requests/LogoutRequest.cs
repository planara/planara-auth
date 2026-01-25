namespace Planara.Auth.Requests;

/// <summary>
/// Запрос на выход из аккаунта
/// </summary>
public sealed class LogoutRequest
{
    public required string RefreshToken { get; set; }
}