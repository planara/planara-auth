using System.Reflection;
using System.Runtime.CompilerServices;
using HotChocolate;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using Microsoft.EntityFrameworkCore;
using Planara.Auth.Data;
using Planara.Auth.Data.Enums;
using Planara.Auth.Registration;

namespace Planara.Auth.GraphQL;

[AttributeUsage(AttributeTargets.Method)]
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
                var registrationContext = middlewareContext.GetGlobalStateOrDefault<RegistrationRequestContext>(RegistrationRequest.ContextKey);

                if (registrationContext is null || !registrationContext.HasSession)
                {
                    throw new GraphQLException(ErrorBuilder.New()
                        .SetCode("REGISTRATION_SESSION_REQUIRED")
                        .SetMessage("Сессия регистрации отсутствует.")
                        .Build());
                }

                if (registrationContext.RequiredStep != _requiredStep)
                {
                    throw new GraphQLException(ErrorBuilder.New()
                        .SetCode("REGISTRATION_STEP_NOT_AVAILABLE")
                        .SetMessage("Этот шаг регистрации сейчас недоступен.")
                        .SetExtension("nextStep", registrationContext.RequiredStep?.ToString())
                        .Build());
                }

                var dataContext = middlewareContext.Services.GetRequiredService<DataContext>();

                var registration = await dataContext.RegistrationSessions
                        .SingleOrDefaultAsync(x =>
                            x.Id == registrationContext.RegistrationId!.Value &&
                            x.ExpiresAt > DateTime.UtcNow,
                            middlewareContext.RequestAborted);

                if (registration is null)
                {
                    throw new GraphQLException(ErrorBuilder.New()
                        .SetCode("REGISTRATION_SESSION_EXPIRED")
                        .SetMessage("Сессия регистрации отсутствует или истекла.")
                        .Build());
                }

                middlewareContext.SetLocalState(RegistrationRequest.SessionKey, registration);

                await next(middlewareContext);
            });
    }
}