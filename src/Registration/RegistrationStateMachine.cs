using Planara.Auth.Data.Domain;
using Planara.Auth.Data.Enums;

namespace Planara.Auth.Registration;

public static class RegistrationStateMachine
{
    public static RegistrationStep GetRequiredStep(RegistrationStep nextStep, RegistrationAuthLevel authLevel) =>
        authLevel switch
        {
            RegistrationAuthLevel.Challenge => RegistrationStep.Code,
            RegistrationAuthLevel.Authorized => nextStep,
            
            _ => throw new ArgumentOutOfRangeException(nameof(authLevel), authLevel, null)
        };

    public static void CompleteEmailVerification(RegistrationSession registration)
    {
        if (registration.CurrentStep == RegistrationStep.Email && registration.NextStep == RegistrationStep.Code)
        {
            registration.IsEmailConfirmed = true;
            registration.CurrentStep = RegistrationStep.Code;
            registration.NextStep = RegistrationStep.Password;
        }
    }

    public static void CompleteStep(RegistrationSession registration, RegistrationStep step)
    {
        if (registration.NextStep != step)
            throw new InvalidOperationException(
                $"Cannot complete registration step '{step}'. Expected '{registration.NextStep}'.");

        registration.CurrentStep = step;
        registration.NextStep = GetNext(step);
    }

    private static RegistrationStep GetNext(RegistrationStep step) =>
        step switch
        {
            RegistrationStep.Code => RegistrationStep.Password,
            RegistrationStep.Password => RegistrationStep.Personal,
            RegistrationStep.Personal => RegistrationStep.Completed, // Завершение регистрации

            _ => throw new InvalidOperationException($"Registration step '{step}' has no next step.")
        };
}