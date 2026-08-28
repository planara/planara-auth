using HotChocolate;

namespace Planara.Auth.Requests;

/// <summary>
/// Запрос на добавление пароля при регистрации
/// </summary>
[GraphQLDescription("Запрос на добавление пароля при регистрации")]
public class SetRegistrationPasswordRequest
{
    /// <summary>
    /// Пароль пользователя
    /// </summary>
    [GraphQLDescription("Пароль пользователя")]
    public required string Password { get; init; }

    /// <summary>
    /// Повтор пароля для подтверждения
    /// </summary>
    [GraphQLDescription("Повтор пароля для подтверждения")]
    public required string ConfirmPassword { get; init; }
}