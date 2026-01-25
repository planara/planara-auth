using System.Security.Claims;

namespace Planara.Auth.Services;

/// <summary>
/// Сервис генерации и обработки access и refresh токенов.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Генерирует JWT access token на основе переданных клеймов
    /// </summary>
    /// <param name="claims">Набор клеймов, которые будут включены в токен</param>
    /// <returns>
    /// Кортеж, содержащий:
    /// <list type="bullet">
    /// <item><description>строковое представление JWT access token</description></item>
    /// <item><description>дату и время истечения токена</description></item>
    /// </list>
    /// </returns>
    (string token, DateTime expiresAtUtc) GenerateAccessToken(IEnumerable<Claim> claims);
    
    /// <summary>
    /// Генерирует новый refresh token
    /// </summary>
    /// <returns>
    /// Кортеж, содержащий:
    /// <list type="bullet">
    /// <item><description>raw refresh token, возвращаемый клиенту</description></item>
    /// <item><description>хэш refresh token, предназначенный для хранения в базе данных</description></item>
    /// </list>
    /// </returns>
    (string refreshToken, string refreshTokenHash) GenerateRefreshToken();
    
    /// <summary>
    /// Вычисляет хэш refresh token для последующего сравнения и поиска в базе данных
    /// </summary>
    /// <param name="refreshToken">Raw refresh token</param>
    /// <returns>Хэшированное представление refresh token</returns>
    string HashRefreshToken(string refreshToken);
}