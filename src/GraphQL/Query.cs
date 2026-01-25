using System.Security.Claims;
using HotChocolate;
using HotChocolate.Types;
using Microsoft.AspNetCore.Authorization;
using Planara.Common.Auth.Claims;

namespace Planara.Auth.GraphQL;

[ExtendObjectType(OperationTypeNames.Query)]
public class Query
{
    [Authorize]
    public Guid Me(ClaimsPrincipal user) => user.GetUserId();
}