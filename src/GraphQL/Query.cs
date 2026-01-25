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
    
    public IEnumerable<string> Claims(ClaimsPrincipal user) =>
        user.Claims.Select(c => $"{c.Type}={c.Value}");
    
    public string? AuthHeader([Service] IHttpContextAccessor http)
        => http.HttpContext?.Request.Headers.Authorization.ToString();
}