using AppAny.HotChocolate.FluentValidation;
using HotChocolate.Types;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Planara.Auth.Data;
using Planara.Auth.GraphQL;
using Planara.Common.GraphQL.Filters;
using Planara.Common.Kafka;
using Planara.Kafka.Interfaces;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Planara.Auth.Tests;

public class ApiTestWebAppFactory: WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:latest")
        .WithDatabase("auth-test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();
    
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<DataContext>));
            services.RemoveAll(typeof(DataContext));
            services.RemoveAll(typeof(IKafkaProducer<UserCreatedMessage>));

            services
                .AddGraphQLServer()
                .AddErrorFilter<ErrorFilter>()
                .AddQueryType(m => m.Name(OperationTypeNames.Query))
                .AddType<Query>()
                .AddMutationType(m => m.Name(OperationTypeNames.Mutation))
                .AddType<Mutation>()
                .AddAuthorization()
                .AddFluentValidation(options =>
                {
                    options.UseInputValidators();
                    options.UseDefaultErrorMapper();
                })
                .InitializeOnStartup();

            services.AddScoped<FakeKafkaProducer>();
            services.AddScoped<IKafkaProducer<UserCreatedMessage>>(sp =>
                sp.GetRequiredService<FakeKafkaProducer>());

            services.AddDbContext<DataContext>(opt =>
                opt.UseNpgsql(_postgres.GetConnectionString()));
            
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(_redis.GetConnectionString()));
        });
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();

        db.Database.SetCommandTimeout(3000);
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync() => await _postgres.StopAsync();
}