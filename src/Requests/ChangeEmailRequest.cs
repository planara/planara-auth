namespace Planara.Auth.Requests;

/// <summary>
/// Запрос на начало изменения адреса электронной почты
/// </summary>
public class ChangeEmailRequest
{
    /// <summary>
    /// Новый адрес электронной почты
    /// </summary>
    public required string Email { get; init; }
}