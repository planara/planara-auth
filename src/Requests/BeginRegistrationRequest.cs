using System.ComponentModel.DataAnnotations;
using HotChocolate;

namespace Planara.Auth.Requests;

/// <summary>
/// Запрос для начала регистрации
/// </summary>
[GraphQLDescription("Запрос для начала регистрации")]
public class BeginRegistrationRequest
{
    /// <summary>
    /// Адрес электронной почты пользователя
    /// </summary>
    [EmailAddress]
    [GraphQLDescription("Адрес электронной почты пользователя")]
    public required string Email { get; set; }
    
    /// <summary>
    /// Согласие на обработку персональных данных
    /// </summary>
    [GraphQLDescription("Согласие на обработку персональных данных")]
    public required bool PersonalDataConsent { get; set; }
    
    /// <summary>
    /// ID версии согласия на обработку персональных данных
    /// </summary>
    [GraphQLDescription("ID версии согласия на обработку персональных данных")]
    public required Guid ConsentVersionId { get; set; }
}