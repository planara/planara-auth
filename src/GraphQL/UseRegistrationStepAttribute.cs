using System.Reflection;
using System.Runtime.CompilerServices;
using HotChocolate;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using Planara.Auth.Data.Enums;
using Planara.Auth.Registration;

namespace Planara.Auth.GraphQL;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class UseRegistrationStepAttribute : ObjectFieldDescriptorAttribute
{
    private readonly RegistrationStep _requiredStep;

    public UseRegistrationStepAttribute(RegistrationStep requiredStep, [CallerLineNumber] int order = 0)
    {
        _requiredStep = requiredStep;
        Order = order;
    }

    protected override void OnConfigure(IDescriptorContext context, IObjectFieldDescriptor descriptor, MemberInfo member)
    {
        descriptor.Use(next =>
            async middlewareContext =>
            {
                var registration =
                    middlewareContext.GetGlobalStateOrDefault<RegistrationRequestContext>(RegistrationRequest.ContextKey);

                if (registration is null || !registration.HasSession)
                {
                    throw new GraphQLException(
                        ErrorBuilder.New()
                            .SetCode("REGISTRATION_SESSION_REQUIRED")
                            .SetMessage("Сессия регистрации отсутствует или истекла.")
                            .Build());
                }

                if (registration.RequiredStep != _requiredStep)
                {
                    throw new GraphQLException(
                        ErrorBuilder.New()
                            .SetCode("REGISTRATION_STEP_NOT_AVAILABLE")
                            .SetMessage("Этот шаг регистрации сейчас недоступен.")
                            .SetExtension("nextStep", registration.RequiredStep?.ToString())
                            .Build());
                }

                await next(middlewareContext);
            });
    }
}