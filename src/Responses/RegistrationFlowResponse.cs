using HotChocolate;
using Planara.Auth.Data.Enums;

namespace Planara.Auth.Responses;

/// <summary>
/// Ответ пошаговой регистрации
/// </summary>
[GraphQLDescription("Ответ пошаговой регистрации")]
public class RegistrationFlowResponse
{
    /// <summary>
    /// Токен сессии для URL
    /// </summary>
    public required string SessionToken { get; init; }
    
    /// <summary>
    /// Текущий шаг регистрации
    /// </summary>
    [GraphQLDescription("Текущий шаг регистрации")]
    public RegistrationStep Step { get; set; }
    
    /// <summary>
    /// Следующий шаг регистрации
    /// </summary>
    [GraphQLDescription("Следующий шаг регистрации")]
    public RegistrationStep NextStep { get; set; }
}