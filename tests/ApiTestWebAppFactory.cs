using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Planara.Auth.Data;
using Planara.Auth.Workers;
using Planara.Common.Kafka;
using Planara.Kafka.Interfaces;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Planara.Auth.Tests;

public class ApiTestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:latest")
        .WithDatabase("auth-test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:latest").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<DataContext>));
            services.RemoveAll(typeof(DataContext));
            services.RemoveAll(typeof(IConnectionMultiplexer));

            services.RemoveAll<IKafkaProducer<UserCreatedMessage>>();
            services.RemoveAll<IKafkaProducer<UserDeletedMessage>>();
            services.RemoveAll<IHostedService>();

            services.AddSingleton<FakeKafkaProducer<UserCreatedMessage>>();
            services.AddSingleton<FakeKafkaProducer<UserDeletedMessage>>();

            services.AddSingleton<IKafkaProducer<UserCreatedMessage>>(sp =>
                sp.GetRequiredService<FakeKafkaProducer<UserCreatedMessage>>());

            services.AddSingleton<IKafkaProducer<UserDeletedMessage>>(sp =>
                sp.GetRequiredService<FakeKafkaProducer<UserDeletedMessage>>());

            services.AddScoped<UserCreatedOutboxPublisher>();
            services.AddScoped<UserDeletedOutboxPublisher>();

            services.AddDbContext<DataContext>(opt =>
                opt.UseNpgsql(_postgres.GetConnectionString()));

            services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = TestAuthHandler.AuthenticationScheme;
                    options.DefaultAuthenticateScheme = TestAuthHandler.AuthenticationScheme;
                    options.DefaultChallengeScheme = TestAuthHandler.AuthenticationScheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.AuthenticationScheme,
                    _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.AuthenticationScheme;
                options.DefaultAuthenticateScheme = TestAuthHandler.AuthenticationScheme;
                options.DefaultChallengeScheme = TestAuthHandler.AuthenticationScheme;
            });
        });

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string>(
                    "DbConnections:Redis:ConnectionString",
                    _redis.GetConnectionString()!),
                new KeyValuePair<string, string>(
                    "GraphQL:Name",
                    "test-auth-schema")
            }!);
        });
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();

        db.Database.SetCommandTimeout(3000);
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await _postgres.StopAsync();
        await _redis.StopAsync();
    }
}