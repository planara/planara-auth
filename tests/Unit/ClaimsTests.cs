using System.Security.Claims;
using FluentAssertions;
using Planara.Common.Auth.Claims;
using ClaimTypes = Planara.Common.Auth.Claims.ClaimTypes;

namespace Planara.Auth.Tests.Unit;

public class ClaimsTests
{
    [Fact]
    public void GetUserId_WhenMissing_Throws()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var act = () => principal.GetUserId();

        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void GetUserId_WhenInvalidGuid_Throws()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.UserId, "not-a-guid")
        }));

        var act = () => principal.GetUserId();

        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void GetUserId_WhenValid_ReturnsGuid()
    {
        var id = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.UserId, id.ToString())
        }));

        principal.GetUserId().Should().Be(id);
    }
}