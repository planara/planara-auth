using FluentAssertions;
using Planara.Auth.Data.Domain;
using Planara.Auth.Data.Enums;
using Planara.Auth.Registration;

namespace Planara.Auth.Tests.Unit;

public class RegistrationStateMachineTests
{
    [Fact]
    public void GetRequiredStep_ChallengeAndCode_ReturnsCode()
    {
        var result = RegistrationStateMachine.GetRequiredStep(RegistrationStep.Code, RegistrationAuthLevel.Challenge);

        result.Should().Be(RegistrationStep.Code);
    }

    [Fact]
    public void GetRequiredStep_ChallengeAndPassword_ReturnsCode()
    {
        var result = RegistrationStateMachine.GetRequiredStep(RegistrationStep.Password, RegistrationAuthLevel.Challenge);

        result.Should().Be(RegistrationStep.Code);
    }

    [Fact]
    public void GetRequiredStep_AuthorizedAndPassword_ReturnsPassword()
    {
        var result = RegistrationStateMachine.GetRequiredStep(RegistrationStep.Password, RegistrationAuthLevel.Authorized);

        result.Should().Be(RegistrationStep.Password);
    }

    [Fact]
    public void GetRequiredStep_AuthorizedAndPersonal_ReturnsPersonal()
    {
        var result = RegistrationStateMachine.GetRequiredStep(RegistrationStep.Personal, RegistrationAuthLevel.Authorized);

        result.Should().Be(RegistrationStep.Personal);
    }

    [Fact]
    public void GetRequiredStep_InvalidAuthLevel_Throws()
    {
        var action = () => RegistrationStateMachine.GetRequiredStep(RegistrationStep.Code, (RegistrationAuthLevel)999);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CompleteEmailVerification_ValidState_CompletesCodeStep()
    {
        var registration = CreateRegistration(RegistrationStep.Email, RegistrationStep.Code);
        RegistrationStateMachine.CompleteEmailVerification(registration);

        registration.IsEmailConfirmed.Should().BeTrue();
        registration.CurrentStep.Should().Be(RegistrationStep.Code);
        registration.NextStep.Should().Be(RegistrationStep.Password);
    }

    [Fact]
    public void CompleteEmailVerification_InvalidCurrentStep_DoesNothing()
    {
        var registration = CreateRegistration(RegistrationStep.Code, RegistrationStep.Password);
        RegistrationStateMachine.CompleteEmailVerification(registration);

        registration.IsEmailConfirmed.Should().BeFalse();
        registration.CurrentStep.Should().Be(RegistrationStep.Code);
        registration.NextStep.Should().Be(RegistrationStep.Password);
    }

    [Fact]
    public void CompleteEmailVerification_InvalidNextStep_DoesNothing()
    {
        var registration = CreateRegistration(RegistrationStep.Email, RegistrationStep.Password);
        RegistrationStateMachine.CompleteEmailVerification(registration);

        registration.IsEmailConfirmed.Should().BeFalse();
        registration.CurrentStep.Should().Be(RegistrationStep.Email);
        registration.NextStep.Should().Be(RegistrationStep.Password);
    }

    [Fact]
    public void CompleteStep_Password_CompletesPasswordStep()
    {
        var registration = CreateRegistration(RegistrationStep.Code, RegistrationStep.Password);
        RegistrationStateMachine.CompleteStep(registration, RegistrationStep.Password);

        registration.CurrentStep.Should().Be(RegistrationStep.Password);
        registration.NextStep.Should().Be(RegistrationStep.Personal);
    }

    [Fact]
    public void CompleteStep_Personal_CompletesPersonalStep()
    {
        var registration = CreateRegistration(RegistrationStep.Password, RegistrationStep.Personal);
        RegistrationStateMachine.CompleteStep(registration, RegistrationStep.Personal);

        registration.CurrentStep.Should().Be(RegistrationStep.Personal);
        registration.NextStep.Should().Be(RegistrationStep.Completed);
    }

    [Fact]
    public void CompleteStep_UnexpectedStep_Throws()
    {
        var registration = CreateRegistration(RegistrationStep.Code, RegistrationStep.Password);
        var action = () => RegistrationStateMachine.CompleteStep(registration, RegistrationStep.Personal);

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetNext_Code_ReturnsPassword()
    {
        RegistrationStateMachine
            .GetNext(RegistrationStep.Code)
            .Should()
            .Be(RegistrationStep.Password);
    }

    [Fact]
    public void GetNext_Password_ReturnsPersonal()
    {
        RegistrationStateMachine
            .GetNext(RegistrationStep.Password)
            .Should()
            .Be(RegistrationStep.Personal);
    }

    [Fact]
    public void GetNext_Personal_ReturnsCompleted()
    {
        RegistrationStateMachine
            .GetNext(RegistrationStep.Personal)
            .Should()
            .Be(RegistrationStep.Completed);
    }

    [Fact]
    public void GetNext_Email_Throws()
    {
        var action = () => RegistrationStateMachine.GetNext(RegistrationStep.Email);

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetNext_Completed_Throws()
    {
        var action = () => RegistrationStateMachine.GetNext(RegistrationStep.Completed);

        action.Should().Throw<InvalidOperationException>();
    }

    private static RegistrationSession CreateRegistration(RegistrationStep currentStep, RegistrationStep nextStep)
    {
        return new RegistrationSession
        {
            Email = "test@planara.ru",
            SessionTokenHash = "hash",
            CurrentStep = currentStep,
            NextStep = nextStep,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    }
}