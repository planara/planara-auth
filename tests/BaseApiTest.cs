using Microsoft.Extensions.DependencyInjection;
using Planara.Auth.Data;

namespace Planara.Auth.Tests;

public class BaseApiTest: IClassFixture<ApiTestWebAppFactory>
{
    protected readonly ApiTestWebAppFactory Factory;
    protected readonly IServiceScope Scope;
    protected readonly DataContext Context;
    protected readonly HttpClient Client;

    protected BaseApiTest(ApiTestWebAppFactory factory)
    {
        Factory = factory;

        Scope = factory.Services.CreateScope();
        Context = Scope.ServiceProvider.GetRequiredService<DataContext>();
        Client = factory.CreateClient();
    }

    public void Dispose()
    {
        Scope.Dispose();
    }
}