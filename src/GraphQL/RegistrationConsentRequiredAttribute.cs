using System.Reflection;
using System.Runtime.CompilerServices;
using HotChocolate;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using Microsoft.EntityFrameworkCore;
using Planara.Auth.Data;
using Planara.Auth.Registration;
using Planara.Common.Enums;

namespace Planara.Auth.GraphQL;

[AttributeUsage(AttributeTargets.Method)]
public class RegistrationConsentRequiredAttribute : ObjectFieldDescriptorAttribute
{
    private readonly ConsentType _consentType;

    public RegistrationConsentRequiredAttribute(ConsentType consentType, [CallerLineNumber] int order = 0)
    {
        _consentType = consentType;
        Order = order;
    }

    protected override void OnConfigure(IDescriptorContext context, IObjectFieldDescriptor descriptor, MemberInfo member)
    {
        descriptor.Use(next => async middlewareContext =>
        {
            var registrationContext = middlewareContext.GetGlobalStateOrDefault<RegistrationRequestContext>(RegistrationRequest.ContextKey);

            if (registrationContext is null || !registrationContext.HasSession)
                throw new GraphQLException(ErrorBuilder.New()
                    .SetCode("REGISTRATION_SESSION_REQUIRED")
                    .SetMessage("Сессия регистрации отсутствует.")
                    .Build());

            var dataContext = middlewareContext.Services.GetRequiredService<DataContext>();

            var isGranted = await dataContext.UserConsentProjections
                .AsNoTracking()
                .AnyAsync(x =>
                    x.UserId == registrationContext.RegistrationId!.Value &&
                    x.Type == _consentType && x.IsGranted, middlewareContext.RequestAborted);

            if (!isGranted)
                throw new GraphQLException(ErrorBuilder.New()
                    .SetCode("CONSENT_REQUIRED")
                    .SetMessage("Согласие на обработку персональных данных ещё не подтверждено.")
                    .Build());

            await next(middlewareContext);
        });
    }
}