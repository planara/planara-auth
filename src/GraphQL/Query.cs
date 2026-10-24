using System.Security.Claims;
using HotChocolate;
using HotChocolate.Types;
using Microsoft.AspNetCore.Authorization;
using Planara.Auth.Registration;
using Planara.Auth.Responses;
using Planara.Common.Auth.Claims;

namespace Planara.Auth.GraphQL;

[ExtendObjectType(OperationTypeNames.Query)]
public class Query
{
    [Authorize]
    public Guid Me(ClaimsPrincipal user) => user.GetUserId();
    
    [AllowAnonymous]
    [GraphQLDescription("Текущее состояние регистрации по токену сессии")]
    public RegistrationFlowResponse? RegistrationFlow(
        [GlobalState(RegistrationRequest.ContextKey)]
        RegistrationRequestContext registrationContext)
    {
        if (!registrationContext.HasSession)
            return null;

        return new RegistrationFlowResponse
        {
            SessionToken = registrationContext.SessionToken!,
            Step = registrationContext.CurrentStep!.Value,
            NextStep = registrationContext.RequiredStep!.Value
        };
    }
}