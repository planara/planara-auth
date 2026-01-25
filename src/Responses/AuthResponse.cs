using HotChocolate;

namespace Planara.Auth.Responses;

/// <summary>
/// Результат успешной аутентификации или обновления токенов
/// </summary>
[GraphQLDescription("Результат успешной аутентификации или обновления токенов")]
public class AuthResponse
{
    /// <summary>
    /// JWT access token
    /// </summary>
    [GraphQLDescription("JWT access token")]
    public string AccessToken { get; set; } = null!;
    
    /// <summary>
    /// Дата и время истечения access token
    /// </summary>
    [GraphQLDescription("Дата и время истечения access token")]
    public DateTime AccessExpiresAtUtc { get; set; }
    
    /// <summary>
    /// Refresh token, предназначенный для дальнейшего обновления access token
    /// </summary>
    [GraphQLDescription("Refresh token, предназначенный для дальнейшего обновления access token")]
    public string RefreshToken { get; set; } = null!;
    
    /// <summary>
    /// Дата и время истечения refresh token
    /// </summary>
    [GraphQLDescription("Дата и время истечения refresh token")]
    public DateTime RefreshExpiresAtUtc { get; set; }
}