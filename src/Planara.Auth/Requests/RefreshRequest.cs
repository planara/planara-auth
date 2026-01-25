namespace Planara.Auth.Requests;

/// <summary>
/// Запрос на обновление access token
/// </summary>
public sealed class RefreshRequest
{
    /// <summary>
    /// Токен для обновления access token
    /// </summary>
    public required string RefreshToken { get; set; }
}