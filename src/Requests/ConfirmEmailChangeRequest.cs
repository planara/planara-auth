namespace Planara.Auth.Requests;

/// <summary>
/// Запрос на подтверждение изменения адреса электронной почты
/// </summary>
public class ConfirmEmailChangeRequest
{
    /// <summary>
    /// Код подтверждения
    /// </summary>
    public required string Code { get; init; }
}