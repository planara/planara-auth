using System.ComponentModel.DataAnnotations;
using HotChocolate;

namespace Planara.Auth.Requests;

/// <summary>
/// Запрос на регистрацию
/// </summary>
[GraphQLDescription("Запрос на регистрацию")]
public sealed class RegisterRequest
{
    /// <summary>
    /// Адрес электронной почты пользователя
    /// </summary>
    [EmailAddress]
    [GraphQLDescription("Адрес электронной почты пользователя")]
    public required string Email { get; set; }
    
    /// <summary>
    /// Пароль пользователя
    /// </summary>
    [GraphQLDescription("Пароль пользователя")]
    public required string Password { get; set; }
}