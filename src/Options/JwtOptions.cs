namespace Planara.Auth.Options;

/// <summary>
/// Настройки для генерации и валидации access token
/// </summary>
public sealed class JwtOptions
{
    /// <summary>
    /// Издатель токена (claim <c>iss</c>)
    /// Используется при валидации подписи и источника токена
    /// </summary>
    public required string Issuer { get; init; }
    
    /// <summary>
    /// Аудитория токена (claim <c>aud</c>)
    /// Используется при валидации назначения токена
    /// </summary>
    public required string Audience { get; init; }
    
    /// <summary>
    /// Секретный ключ для подписи JWT
    /// </summary>
    public required string SigningKey { get; init; }
    
    /// <summary>
    /// Время жизни access token в минутах
    /// </summary>
    public int AccessTokenMinutes { get; init; } = 30;
}
