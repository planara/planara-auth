using HotChocolate;

namespace Planara.Auth.Requests;

/// <summary>
/// Запрос на обновление access token с использованием refresh token
/// </summary>
[GraphQLDescription("Запрос на обновление access token")]
public sealed class RefreshRequest
{
    /// <summary>
    /// Refresh token, используемый для получения нового access token
    /// </summary>
    public required string RefreshToken { get; set; }
}