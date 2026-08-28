using HotChocolate;

namespace Planara.Auth.Requests;

/// <summary>
/// Запрос на подтверждение почты
/// </summary>
[GraphQLDescription("Запрос на подтверждение почты")]
public class VerifyRegistrationCodeRequest
{
    /// <summary>
    /// Код подтверждения почты
    /// </summary>
    [GraphQLDescription("Код подтверждения почты")]
    public required string Code { get; init; }
}