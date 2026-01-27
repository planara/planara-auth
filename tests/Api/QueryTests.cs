using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Planara.Auth.Tests.Api;

[Collection("AuthApi")]
public class QueryTests: BaseApiTest
{
    public QueryTests(ApiTestWebAppFactory factory) : base(factory) { }

    [Fact]
    public async Task Me_WithoutAuthorization_ReturnsError()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        Client.DefaultRequestHeaders.Authorization = null;

        const string query = """
                             query {
                               me
                             }
                             """;

        var doc = await Client.PostAsync(query);

        doc.GetErrors().Should().NotBeNull();
    }

    [Fact]
    public async Task Me_WithValidAccessToken_ReturnsUserId()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        // register → получаем access token
        const string register = """
                                mutation ($request: RegisterRequestInput!) {
                                  register(request: $request) {
                                    accessToken
                                  }
                                }
                                """;

        var email = "me@test.com";

        var reg = await Client.PostAsync(register, new
        {
            request = new { email, password = "Qwerty1!" }
        });

        reg.GetErrors().Should().BeNull();

        var access = reg.GetData().GetProperty("register").GetProperty("accessToken").GetString()!;
        var userId = (await Context.UserCredentials.SingleAsync(x => x.Email == email)).UserId;

        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", access);

        const string query = """
                             query {
                               me
                             }
                             """;

        var doc = await Client.PostAsync(query);

        doc.GetErrors().Should().BeNull();

        var me = doc.GetData().GetProperty("me").GetString();
        me.Should().NotBeNullOrWhiteSpace();
        Guid.Parse(me).Should().Be(userId);
    }
}