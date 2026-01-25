using System.ComponentModel.DataAnnotations;
using HotChocolate;

namespace Planara.Auth.Requests;

/// <summary>
/// Запрос на вход в аккаунт
/// </summary>
[GraphQLDescription("Запрос на вход в аккаунт")]
public sealed class LoginRequest
{
    /// <summary>
    /// Адрес электронной почты
    /// </summary>
    [EmailAddress]
    [GraphQLDescription("Адрес электронной почты пользователя")]
    public required string Email { get; set; }
    
    /// <summary>
    /// Пароль
    /// </summary>
    [GraphQLDescription("Пароль пользователя")]
    public required string Password { get; set; }
}