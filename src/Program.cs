using System.Reflection;
using System.Text;
using AppAny.HotChocolate.FluentValidation;
using HotChocolate.Types;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Planara.Auth.Data;
using Planara.Auth.GraphQL;
using Planara.Auth.Options;
using Planara.Auth.Services;
using Planara.Auth.Workers;
using Planara.Common.Configuration;
using Planara.Common.Database;
using Planara.Common.GraphQL.Filters;
using Planara.Common.Host;
using Planara.Common.Kafka;
using Planara.Common.Validators;
using Planara.Kafka.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddSettingsJson();
builder.Services
    .AddValidators(Assembly.GetExecutingAssembly())
    .AddHttpContextAccessor()
    .AddAuthorization()
    // .AddCors()
    .AddLogging();

builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection("Jwt"))
    .ValidateDataAnnotations()
    .Validate(o => o.SigningKey.Length >= 32, "SigningKey must be at least 32 chars")
    .ValidateOnStart();

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
    .InitializeOnStartup();

builder.Services.AddDataContext<DataContext>(
    builder.Configuration.GetValue<string>("DbConnections:Postgres:ConnectionString")!,
    builder.Configuration.GetValue<int>("DbConnections:Postgres:MaxRetry"),
    builder.Configuration.GetValue<int>("DbConnections:Postgres:MaxDelaySec")
);

builder.Services
    .AddKafkaProducer<UserCreatedMessage>(builder.Configuration)
    .AddKafkaTopicsInitializer(builder.Configuration);

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOpt) =>
    {
        var jwt = jwtOpt.Value;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,

            ValidateAudience = true,
            ValidAudience = jwt.Audience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddScoped<ITokenService, TokenService>();

builder.Services.AddScoped<OutboxPublisher>();
if (!builder.Environment.IsEnvironment("Test"))
    builder.Services.AddHostedService<OutboxPublisher>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

var app = builder.Build();

// Инициализация топиков в Kafka
await app.UseKafka();

app.UseAuthentication();
app.UseAuthorization();
app.MapGraphQL();

app.PrepareAndRun<DataContext>(args);