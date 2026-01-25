using HotChocolate;

namespace Planara.Auth.Requests;

/// <summary>
/// Запрос на выход из аккаунта и отзыв refresh token
/// </summary>
[GraphQLDescription("Запрос на выход из аккаунта")]
public sealed class LogoutRequest
{
    /// <summary>
    /// Refresh token, который необходимо отозвать
    /// </summary>
    public required string RefreshToken { get; set; }
}