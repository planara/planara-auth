namespace Planara.Auth.Requests;

/// <summary>
/// Запрос на изменение адреса электронной почты во время регистрации
/// </summary>
public class ChangeRegistrationEmailRequest
{
    /// <summary>
    /// Новый адрес электронной почты
    /// </summary>
    public required string Email { get; init; }
}