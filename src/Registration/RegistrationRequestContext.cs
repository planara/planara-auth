using Planara.Auth.Data.Enums;

namespace Planara.Auth.Registration;

public record RegistrationRequestContext(
    Guid? RegistrationId,
    string? Email,
    string? SessionToken,
    RegistrationStep? CurrentStep,
    RegistrationStep? RequiredStep,
    RegistrationAuthLevel? AuthLevel)
{
    public bool HasSession =>
        RegistrationId.HasValue &&
        !string.IsNullOrWhiteSpace(SessionToken) &&
        CurrentStep.HasValue &&
        RequiredStep.HasValue &&
        AuthLevel.HasValue;

    public static RegistrationRequestContext Empty { get; } =
        new(
            RegistrationId: null,
            Email: null,
            SessionToken: null,
            CurrentStep: null,
            RequiredStep: null,
            AuthLevel: null);
}