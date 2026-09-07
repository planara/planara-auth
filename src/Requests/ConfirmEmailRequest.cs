namespace Planara.Auth.Requests;

/// <summary>
/// Запрос на подтверждение текущего адреса электронной почты
/// </summary>
public class ConfirmEmailRequest
{
    /// <summary>
    /// Код подтверждения
    /// </summary>
    public required string Code { get; init; }
}