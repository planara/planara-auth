using System.Reflection;
using AppAny.HotChocolate.FluentValidation;
using HotChocolate.Types;
using Planara.Auth.Data;
using Planara.Auth.GraphQL;
using Planara.Auth.Services;
using Planara.Auth.Workers;
using Planara.Common.Auth.Jwt;
using Planara.Common.Configuration;
using Planara.Common.Database;
using Planara.Common.GraphQL;
using Planara.Common.GraphQL.Filters;
using Planara.Common.GraphQL.Fusion;
using Planara.Common.Host;
using Planara.Common.Kafka;
using Planara.Common.Kafka.Messages.Auth;
using Planara.Common.Kafka.Messages.Notifications;
using Planara.Common.Kafka.Messages.Privacy;
using Planara.Common.Validators;
using Planara.Kafka.Extensions;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.AddSettingsJson();
builder.Services
    .AddValidators(Assembly.GetExecutingAssembly())
    .AddHttpContextAccessor()
    .AddJwtAuth(builder.Configuration)
    // .AddCors()
    .AddLogging();

// GraphQL
builder.Services
    .AddRouting()
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
    .ModifyRequestOptions(o => o.IncludeExceptionDetails = builder.Environment.IsDevelopment())
    .PublishSchemaToRedis(
        _ =>
            ConnectionMultiplexer.Connect(
                builder.Configuration.GetValue<string>("DbConnections:Redis:ConnectionString")!,
                c => c.CertificateValidation += (_, _, _, _) => true
            ),
        builder.Configuration.GetValue<string>("GraphQL:Name")!,
        WellKnownSchema.Auth
    )
    .AddHttpRequestInterceptor<RegistrationHttpRequestInterceptor>()
    .InitializeOnStartup();

// Database
builder.Services.AddDataContext<DataContext>(
    builder.Configuration.GetValue<string>("DbConnections:Postgres:ConnectionString")!,
    builder.Configuration.GetValue<int>("DbConnections:Postgres:MaxRetry"),
    builder.Configuration.GetValue<int>("DbConnections:Postgres:MaxDelaySec")
);

// Kafka
builder.Services
    .AddKafkaProducer<UserCreatedMessage>(builder.Configuration)
    .AddKafkaProducer<UserDeletedMessage>(builder.Configuration)
    .AddKafkaProducer<EmailConfirmationMessage>(builder.Configuration)
    .AddKafkaProducer<ConsentGrantRequestedMessage>(builder.Configuration)
    .AddKafkaConsumer<ConsentGrantedMessage>(builder.Configuration)
    .AddKafkaTopicsInitializer(builder.Configuration);

// Services
builder.Services
    .AddScoped<ITokenService, TokenService>()
    .AddSingleton<IRegistrationCryptoService, RegistrationCryptoService>();

// Hosted
builder.Services
    .AddHostedService<UserCreatedOutboxPublisher>()
    .AddHostedService<UserDeletedOutboxPublisher>()
    .AddHostedService<EmailConfirmationOutboxPublisher>()
    .AddHostedService<ConsentGrantRequestedOutboxPublisher>()
    .AddHostedService<ConsentGrantedKafkaConsumerWorker>()
    .AddHostedService<RegistrationCleanupWorker>()
    .AddHostedService<RefreshTokenCleanupWorker>()
    .AddHostedService<OutboxCleanupWorker>();

var app = builder.Build();

// Инициализация топиков в Kafka
if (!builder.Environment.IsEnvironment("Test"))
    await app.UseKafka();

app.UseAuthentication();
app.UseAuthorization();

app.MapGraphQL();

app.PrepareAndRun<DataContext>(args);