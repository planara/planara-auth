using Microsoft.Extensions.DependencyInjection;
using Planara.Auth.Data;

namespace Planara.Auth.Tests;

public abstract class BaseApiTest : IClassFixture<ApiTestWebAppFactory>, IDisposable
{
    protected readonly ApiTestWebAppFactory Factory;
    protected readonly IServiceScope Scope;
    protected readonly DataContext Context;
    protected readonly HttpClient Client;

    protected readonly Guid UserId = Guid.NewGuid();

    protected BaseApiTest(ApiTestWebAppFactory factory)
    {
        Factory = factory;

        Scope = factory.Services.CreateScope();
        Context = Scope.ServiceProvider.GetRequiredService<DataContext>();

        Client = factory.CreateClient();
        Client.AsUser(UserId);
    }

    public void Dispose()
    {
        Client.Dispose();
        Scope.Dispose();
    }
}