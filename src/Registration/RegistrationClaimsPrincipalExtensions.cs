using System.Security.Claims;

namespace Planara.Auth.Registration;

public static class RegistrationClaimsPrincipalExtensions
{
    public static RegistrationAuthLevel GetRegistrationAuthLevel(this ClaimsPrincipal claims)
    {
        var raw = claims.FindFirst(RegistrationClaimTypes.AuthLevel)?.Value;

        if (!Enum.TryParse<RegistrationAuthLevel>(raw, ignoreCase: true, out var level))
            throw new UnauthorizedAccessException("Missing or invalid registration auth level claim.");

        return level;
    }
}