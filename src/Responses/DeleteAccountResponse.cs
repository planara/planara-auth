using HotChocolate;

namespace Planara.Auth.Responses;

/// <summary>
/// Результат операции удаления аккаунта
/// </summary>
[GraphQLDescription("Результат операции удаления аккаунта")]
public class DeleteAccountResponse
{
    /// <summary>
    /// Признак успешного выполнения операции
    /// </summary>
    [GraphQLDescription("Признак успешного выполнения операции")]
    public bool Success { get; set; }
}