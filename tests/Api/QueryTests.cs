using FluentAssertions;

namespace Planara.Auth.Tests.Api;

public class QueryTests : BaseApiTest
{
    public QueryTests(ApiTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Me_WithoutAuthorization_ReturnsError()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        Client.DefaultRequestHeaders.Remove("X-Test-UserId");

        const string query = """
                             query {
                               me
                             }
                             """;

        using var doc = await Client.PostAsync(query);

        doc.GetErrors().Should().NotBeNull();
    }

    [Fact]
    public async Task Me_WithAuthorizedUser_ReturnsUserId()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        Client.AsUser(UserId);

        const string query = """
                             query {
                               me
                             }
                             """;

        using var doc = await Client.PostAsync(query);

        doc.GetErrors().Should().BeNull();

        var me = doc.GetData()
            .GetProperty("me")
            .GetString();

        me.Should().NotBeNullOrWhiteSpace();
        Guid.Parse(me!).Should().Be(UserId);
    }
}