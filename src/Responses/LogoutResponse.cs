using HotChocolate;

namespace Planara.Auth.Responses;

/// <summary>
/// Результат операции выхода из аккаунта
/// </summary>
[GraphQLDescription("Результат операции выхода из аккаунта")]
public class LogoutResponse
{
    /// <summary>
    /// Признак успешного выполнения операции
    /// </summary>
    [GraphQLDescription("Признак успешного выполнения операции")]
    public bool Success { get; set; }
}