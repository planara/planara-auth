using HotChocolate;

namespace Planara.Auth.Requests;

/// <summary>
/// Запрос на добавление персональных данных при регистрации
/// </summary>
[GraphQLDescription("Запрос на добавление персональных данных при регистрации")]
public class SetRegistrationPersonalRequest
{
    /// <summary>
    /// Имя пользователя
    /// </summary>
    [GraphQLDescription("Имя пользователя")]
    public string? Name { get; init; }

    /// <summary>
    /// Фамилия пользователя
    /// </summary>
    [GraphQLDescription("Фамилия пользователя")]
    public string? Surname { get; init; }
}