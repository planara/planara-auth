using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planara.Auth.Data.Domain;
using Planara.Auth.Data.Enums;
using Planara.Auth.GraphQL;
using Planara.Auth.Registration;
using Planara.Auth.Requests;
using Planara.Auth.Services;
using Planara.Common.Database.Domain;
using Planara.Common.Enums;
using Planara.Common.Kafka;
using Planara.Common.Kafka.Messages.Auth;
using Planara.Common.Kafka.Messages.Notifications;
using Planara.Common.Kafka.Messages.Privacy;
using Planara.Kafka.Configurations;

namespace Planara.Auth.Tests.Api;

public class MutationsTests: BaseApiTest
{
    public MutationsTests(ApiTestWebAppFactory factory) : base(factory) { }

    private async Task ResetAsync() => await DbTestUtils.ResetAuthDbAsync(Context);

    [Fact]
    public async Task Login_WrongPassword_ReturnsError()
    {
        await ResetAsync();

        var userId = Guid.NewGuid();
        Context.UserCredentials.Add(new UserCredential
        {
            UserId = userId,
            Email = "a@b.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Right1!", workFactor: 12),
        });
        await Context.SaveChangesAsync();

        const string mutation = """
        mutation ($login: LoginRequestInput!) {
          login(login: $login) { accessToken }
        }
        """;

        var doc = await Client.PostAsync(mutation, new
        {
            login = new { email = "a@b.com", password = "Wrong1!" }
        });

        doc.GetErrors().Should().NotBeNull();
    }
    
    [Fact]
    public async Task Login_UserNotFound_ReturnsError()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        const string mutation = """
        mutation ($login: LoginRequestInput!) {
          login(login: $login) {
            accessToken
          }
        }
        """;

        var doc = await Client.PostAsync(mutation, new
        {
            login = new { email = "missing@x.com", password = "Qwerty1!" }
        });

        doc.GetErrors().Should().NotBeNull();
    }

    [Fact]
    public async Task Login_Success_ReturnsTokens_AndStoresRefreshToken()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var userId = Guid.NewGuid();
        Context.UserCredentials.Add(new UserCredential
        {
            UserId = userId,
            Email = "a@b.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Right1!", workFactor: 12),
        });
        await Context.SaveChangesAsync();

        const string mutation = """
        mutation ($login: LoginRequestInput!) {
          login(login: $login) {
            accessToken
            refreshToken
            accessExpiresAtUtc
            refreshExpiresAtUtc
          }
        }
        """;

        var doc = await Client.PostAsync(mutation, new
        {
            login = new { email = "  A@B.COM ", password = "Right1!" }
        });

        doc.GetErrors().Should().BeNull();

        var data = doc.GetData().GetProperty("login");
        data.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();

        var rts = await Context.RefreshTokens.Where(x => x.UserId == userId).ToListAsync();
        rts.Should().HaveCount(1);
        rts[0].RevokedAtUtc.Should().BeNull();
    }
    
    [Fact]
    public async Task DeleteAccount_WhenUserExists_RemovesCredentialAndRefreshTokens_AndCreatesUserDeletedOutboxMessage()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "delete@planara.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
        });

        Context.RefreshTokens.AddRange(
            new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                TokenHash = "hash-1",
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(30)
            },
            new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                TokenHash = "hash-2",
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(30)
            });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation DeleteAccount {
                                  deleteAccount {
                                    success
                                  }
                                }
                                """;

        using var json = await Client.PostAsync(mutation);

        json.GetErrors().Should().BeNull();

        json.GetData()
            .GetProperty("deleteAccount")
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue();

        Context.ChangeTracker.Clear();

        var credentialExists = await Context.UserCredentials
            .AsNoTracking()
            .AnyAsync(x => x.UserId == UserId);

        credentialExists.Should().BeFalse();

        var refreshTokensCount = await Context.RefreshTokens.AsNoTracking()
            .CountAsync(x => x.UserId == UserId);

        refreshTokensCount.Should().Be(0);

        var outbox = await Context.OutboxMessages.AsNoTracking().SingleAsync();

        outbox.TopicKey.Should().Be(KafkaTopicKeys.UserDeleted);
        outbox.Type.Should().Be(nameof(UserDeletedMessage));
        outbox.Key.Should().Be(UserId.ToString("N"));
    }
    
    [Fact]
    public async Task DeleteAccount_WhenCredentialDoesNotExist_ReturnsSuccess_AndDoesNotCreateOutboxMessage()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        const string mutation = """
                                mutation DeleteAccount {
                                  deleteAccount {
                                    success
                                  }
                                }
                                """;

        using var json = await Client.PostAsync(mutation);

        json.GetErrors().Should().BeNull();

        json.GetData()
            .GetProperty("deleteAccount")
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue();

        var outboxCount = await Context.OutboxMessages.CountAsync();

        outboxCount.Should().Be(0);
    }
    
    [Fact]
    public async Task BeginRegistration_NewEmail_CreatesRegistrationVerificationAndOutboxes()
    {
        await ResetAsync();
    
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
    
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Planara.Tests/1.0");
    
        var consentVersionId = Guid.NewGuid();
    
        const string mutation = """
                                mutation ($request: BeginRegistrationRequestInput!) {
                                  beginRegistration(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;
    
        using var doc = await client.PostAsync(mutation, new
        {
            request = new
            {
                email = "  NEW@EXAMPLE.COM ",
                consentVersionId,
                personalDataConsent = true
            }
        });
    
        doc.GetErrors().Should().BeNull();
    
        var data = doc.GetData().GetProperty("beginRegistration");
    
        var sessionToken = data.GetProperty("sessionToken").GetString();
    
        sessionToken.Should().NotBeNullOrWhiteSpace();
    
        Context.ChangeTracker.Clear();
    
        var registration = await Context.RegistrationSessions
            .AsNoTracking()
            .SingleAsync();
    
        registration.Email.Should().Be("new@example.com");
        registration.CurrentStep.Should().Be(RegistrationStep.Email);
        registration.NextStep.Should().Be(RegistrationStep.Code);
        registration.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    
        var verification = await Context.RegistrationEmailVerifications
            .AsNoTracking()
            .SingleAsync();
    
        verification.RegistrationSessionId.Should().Be(registration.Id);
        verification.Attempts.Should().Be(0);
        verification.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        verification.ResendAvailableAt.Should().BeAfter(DateTime.UtcNow);
    
        var outboxes = await Context.OutboxMessages
            .AsNoTracking()
            .ToListAsync();
    
        outboxes.Should().HaveCount(2);
    
        var consentOutbox = outboxes.Single(x => x.TopicKey == KafkaTopicKeys.ConsentGrantRequested);
    
        consentOutbox.Type.Should().Be(nameof(ConsentGrantRequestedMessage));
    
        var consent = JsonSerializer.Deserialize<ConsentGrantRequestedMessage>(consentOutbox.PayloadJson, KafkaJson.SerializerOptions);
    
        consent.Should().NotBeNull();
        consent.RegistrationId.Should().Be(registration.Id);
        consent.UserId.Should().BeNull();
        consent.Type.Should().Be(ConsentType.PersonalData);
        consent.ConsentVersionId.Should().Be(consentVersionId);
        consent.ExpiresAt.Should().BeCloseTo(registration.ExpiresAt, TimeSpan.FromMicroseconds(1));
        consent.UserAgent.Should().Be("Planara.Tests/1.0");
    
        outboxes.Should().ContainSingle(x =>
            x.TopicKey == KafkaTopicKeys.EmailConfirmation &&
            x.Type == nameof(EmailConfirmationMessage));
    }
    
    [Fact]
    public async Task BeginRegistration_RegisteredEmail_ReturnsError()
    {
        await ResetAsync();
    
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
    
        Context.UserCredentials.Add(new UserCredential
        {
            UserId = Guid.NewGuid(),
            Email = "registered@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!")
        });
    
        await Context.SaveChangesAsync();
    
        const string mutation = """
                                mutation ($request: BeginRegistrationRequestInput!) {
                                  beginRegistration(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;
    
        using var doc = await client.PostAsync(mutation, new
        {
            request = new
            {
                email = "  REGISTERED@EXAMPLE.COM ",
                consentVersionId = Guid.NewGuid(),
                personalDataConsent = true
            }
        });
    
        var errors = doc.GetErrors();
    
        errors.Should().NotBeNull();
    
        errors.Value[0]
            .GetProperty("message")
            .GetString()
            .Should()
            .Be("Пользователь с таким адресом электронной почты уже зарегистрирован.");
    
        Context.ChangeTracker.Clear();
    
        (await Context.RegistrationSessions.AsNoTracking().CountAsync()).Should().Be(0);
        (await Context.OutboxMessages.AsNoTracking().CountAsync()).Should().Be(0);
    }
    
    [Fact]
    public async Task BeginRegistration_ExistingRegistration_RestoresRegistrationAndRegeneratesSessionToken()
    {
        await ResetAsync();
    
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var oldSessionToken = cryptoService.GenerateSessionToken();
        var oldSessionTokenHash = cryptoService.HashSessionToken(oldSessionToken);
    
        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "existing@example.com",
            SessionTokenHash = oldSessionTokenHash,
            CurrentStep = RegistrationStep.Email,
            NextStep = RegistrationStep.Code,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    
        Context.RegistrationSessions.Add(registration);
    
        await Context.SaveChangesAsync();
    
        const string mutation = """
                                mutation ($request: BeginRegistrationRequestInput!) {
                                  beginRegistration(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;
    
        using var doc = await client.PostAsync(mutation, new
        {
            request = new
            {
                email = "existing@example.com",
                consentVersionId = Guid.NewGuid(),
                personalDataConsent = true
            }
        });
    
        doc.GetErrors().Should().BeNull();
    
        var data = doc.GetData().GetProperty("beginRegistration");
        var newSessionToken = data.GetProperty("sessionToken").GetString();
    
        newSessionToken.Should().NotBeNullOrWhiteSpace();
        newSessionToken.Should().NotBe(oldSessionToken);
    
        Context.ChangeTracker.Clear();
    
        var stored = await Context.RegistrationSessions
            .AsNoTracking()
            .SingleAsync();
    
        stored.Id.Should().Be(registration.Id);
        stored.SessionTokenHash.Should().NotBe(oldSessionTokenHash);
    
        cryptoService
            .VerifySessionToken(newSessionToken, stored.SessionTokenHash)
            .Should()
            .BeTrue();
    
        var verification = await Context.RegistrationEmailVerifications
            .AsNoTracking()
            .SingleAsync();
    
        verification.RegistrationSessionId.Should().Be(registration.Id);
    
        var outboxes = await Context.OutboxMessages.AsNoTracking().ToListAsync();
    
        outboxes.Should().ContainSingle(x => x.TopicKey == KafkaTopicKeys.EmailConfirmation);
        outboxes.Should().NotContain(x => x.TopicKey == KafkaTopicKeys.ConsentGrantRequested);
    }
    
    [Fact]
    public async Task BeginRegistration_ExpiredRegistration_RemovesOldAndCreatesNew()
    {
        await ResetAsync();
    
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
    
        var oldSessionToken = cryptoService.GenerateSessionToken();
    
        var expiredRegistration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "expired@example.com",
            SessionTokenHash = cryptoService.HashSessionToken(oldSessionToken),
            CurrentStep = RegistrationStep.Email,
            NextStep = RegistrationStep.Code,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10)
        };
    
        var expiredRegistrationId = expiredRegistration.Id;
    
        Context.RegistrationSessions.Add(expiredRegistration);
    
        await Context.SaveChangesAsync();
    
        const string mutation = """
                                mutation ($request: BeginRegistrationRequestInput!) {
                                  beginRegistration(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;
    
        using var doc = await client.PostAsync(mutation, new
        {
            request = new
            {
                email = "expired@example.com",
                consentVersionId = Guid.NewGuid(),
                personalDataConsent = true
            }
        });
    
        doc.GetErrors().Should().BeNull();
    
        Context.ChangeTracker.Clear();
    
        var registrations = await Context.RegistrationSessions.AsNoTracking()
            .Where(x => x.Email == "expired@example.com")
            .ToListAsync();
    
        registrations.Should().ContainSingle();
    
        var createdRegistration = registrations.Single();
    
        createdRegistration.Id.Should().NotBe(expiredRegistrationId);
        createdRegistration.CurrentStep.Should().Be(RegistrationStep.Email);
        createdRegistration.NextStep.Should().Be(RegistrationStep.Code);
    
        createdRegistration.ExpiresAt
            .Should()
            .BeAfter(DateTime.UtcNow);
    
        var expiredExists = await Context.RegistrationSessions.AsNoTracking()
            .AnyAsync(x => x.Id == expiredRegistrationId);
    
        expiredExists.Should().BeFalse();
    
        var verification = await Context.RegistrationEmailVerifications
            .AsNoTracking()
            .SingleAsync();
    
        verification.RegistrationSessionId.Should().Be(createdRegistration.Id);
    
        var outboxes = await Context.OutboxMessages
            .AsNoTracking()
            .ToListAsync();
    
        outboxes.Should().ContainSingle(x => x.TopicKey == KafkaTopicKeys.ConsentGrantRequested);
        outboxes.Should().ContainSingle(x => x.TopicKey == KafkaTopicKeys.EmailConfirmation);
    }
    
    [Fact]
    public async Task BeginRegistration_ActiveBrowserRegistrationWithSameEmail_ReturnsExistingRegistration()
    {
        await ResetAsync();
    
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
    
        var consentVersionId = Guid.NewGuid();
    
        const string mutation = """
                                mutation ($request: BeginRegistrationRequestInput!) {
                                  beginRegistration(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;
        
        using var firstDoc = await client.PostAsync(mutation, new
        {
            request = new
            {
                email = "resume@example.com",
                consentVersionId,
                personalDataConsent = true
            }
        });
    
        firstDoc.GetErrors().Should().BeNull();
    
        var firstData = firstDoc.GetData().GetProperty("beginRegistration");
        var sessionToken = firstData.GetProperty("sessionToken").GetString();
    
        sessionToken.Should().NotBeNullOrWhiteSpace();
    
        Context.ChangeTracker.Clear();
    
        var registration = await Context.RegistrationSessions.AsNoTracking().SingleAsync();
        var registrationId = registration.Id;
        var outboxCountBefore = await Context.OutboxMessages.AsNoTracking().CountAsync();
    
        outboxCountBefore.Should().Be(2);
        
        client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
        
        using var secondDoc = await client.PostAsync(mutation, new
        {
            request = new
            {
                email = "resume@example.com",
                consentVersionId,
                personalDataConsent = true
            }
        });
    
        secondDoc.GetErrors().Should().BeNull();
    
        var secondData = secondDoc.GetData().GetProperty("beginRegistration");
    
        secondData.GetProperty("sessionToken")
            .GetString()
            .Should()
            .Be(sessionToken);
    
        Context.ChangeTracker.Clear();
    
        var registrations = await Context.RegistrationSessions
            .AsNoTracking()
            .ToListAsync();
    
        registrations.Should().ContainSingle();
        registrations[0].Id.Should().Be(registrationId);
        
        var outboxCountAfter = await Context.OutboxMessages
            .AsNoTracking()
            .CountAsync();
    
        outboxCountAfter.Should().Be(2);
    }
    
    [Fact]
    public async Task BeginRegistration_ActiveBrowserRegistrationWithDifferentEmail_ReturnsMismatchError()
    {
        await ResetAsync();
    
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
    
        const string mutation = """
                                mutation ($request: BeginRegistrationRequestInput!) {
                                  beginRegistration(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;
    
        var consentVersionId = Guid.NewGuid();
        
        using var firstDoc = await client.PostAsync(mutation, new
        {
            request = new
            {
                email = "first@example.com",
                consentVersionId,
                personalDataConsent = true
            }
        });
    
        firstDoc.GetErrors().Should().BeNull();
    
        var sessionToken = firstDoc.GetData()
            .GetProperty("beginRegistration")
            .GetProperty("sessionToken")
            .GetString();
    
        sessionToken.Should().NotBeNullOrWhiteSpace();
    
        Context.ChangeTracker.Clear();
    
        var initialRegistration = await Context.RegistrationSessions
            .AsNoTracking()
            .SingleAsync();
    
        var registrationId = initialRegistration.Id;
    
        client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);

        using var secondDoc = await client.PostAsync(mutation, new
        {
            request = new
            {
                email = "second@example.com",
                consentVersionId = Guid.NewGuid(),
                personalDataConsent = true
            }
        });
    
        var errors = secondDoc.GetErrors();
    
        errors.Should().NotBeNull();
    
        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("REGISTRATION_EMAIL_MISMATCH");
    
        Context.ChangeTracker.Clear();
    
        var registrations = await Context.RegistrationSessions
            .AsNoTracking()
            .ToListAsync();
    
        registrations.Should().ContainSingle();
        registrations[0].Id.Should().Be(registrationId);
        registrations[0].Email.Should().Be("first@example.com");
    
        (await Context.OutboxMessages.AsNoTracking().CountAsync()).Should().Be(2);
    }
    
    [Fact]
    public async Task VerifyRegistrationCode_WhenVerificationDoesNotExist_ReturnsNotFoundError()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var sessionToken = cryptoService.GenerateSessionToken();
    
        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "verify-not-found@example.com",
            SessionTokenHash = cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Email,
            NextStep = RegistrationStep.Code,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    
        Context.RegistrationSessions.Add(registration);
        await Context.SaveChangesAsync();
    
        var jwt = tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Challenge,
            registration.ExpiresAt);
    
        Client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
    
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{RegistrationRequest.CookieName}={jwt}");
    
        const string mutation = """
                                mutation ($request: VerifyRegistrationCodeRequestInput!) {
                                  verifyRegistrationCode(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code = "1234"
            }
        });
    
        var errors = doc.GetErrors();
    
        errors.Should().NotBeNull();
    
        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("REGISTRATION_CODE_NOT_FOUND");
    }
    
    [Fact]
    public async Task VerifyRegistrationCode_WhenCodeExpired_ReturnsExpiredError()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var sessionToken = cryptoService.GenerateSessionToken();
    
        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "verify-expired@example.com",
            SessionTokenHash = cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Email,
            NextStep = RegistrationStep.Code,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    
        Context.RegistrationSessions.Add(registration);
    
        Context.RegistrationEmailVerifications.Add(
            new RegistrationEmailVerification
            {
                RegistrationSessionId = registration.Id,
                CodeHash = cryptoService.HashCode("1234"),
                Attempts = 0,
                ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
                ResendAvailableAt = DateTime.UtcNow.AddMinutes(-1)
            });
    
        await Context.SaveChangesAsync();
    
        var jwt = tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Challenge,
            registration.ExpiresAt);
    
        Client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{RegistrationRequest.CookieName}={jwt}");
    
        const string mutation = """
                                mutation ($request: VerifyRegistrationCodeRequestInput!) {
                                  verifyRegistrationCode(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code = "1234"
            }
        });
    
        var errors = doc.GetErrors();
    
        errors.Should().NotBeNull();
    
        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("REGISTRATION_CODE_EXPIRED");
    
        Context.ChangeTracker.Clear();
    
        var verification = await Context.RegistrationEmailVerifications
            .AsNoTracking()
            .SingleAsync(x => x.RegistrationSessionId == registration.Id);
    
        verification.Attempts.Should().Be(0);
    }
    
    [Fact]
    public async Task VerifyRegistrationCode_WhenAttemptsExceeded_ReturnsAttemptsExceededError()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var sessionToken = cryptoService.GenerateSessionToken();
    
        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "verify-attempts@example.com",
            SessionTokenHash = cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Email,
            NextStep = RegistrationStep.Code,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    
        Context.RegistrationSessions.Add(registration);
    
        Context.RegistrationEmailVerifications.Add(
            new RegistrationEmailVerification
            {
                RegistrationSessionId = registration.Id,
                CodeHash = cryptoService.HashCode("1234"),
                Attempts = RegistrationVerification.MaxAttempts,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                ResendAvailableAt = DateTime.UtcNow.AddMinutes(1)
            });
    
        await Context.SaveChangesAsync();
    
        var jwt = tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Challenge,
            registration.ExpiresAt);
    
        Client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{RegistrationRequest.CookieName}={jwt}");
    
        const string mutation = """
                                mutation ($request: VerifyRegistrationCodeRequestInput!) {
                                  verifyRegistrationCode(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code = "1234"
            }
        });
    
        var errors = doc.GetErrors();
    
        errors.Should().NotBeNull();
    
        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("REGISTRATION_CODE_ATTEMPTS_EXCEEDED");
    
        Context.ChangeTracker.Clear();
    
        var verification = await Context.RegistrationEmailVerifications.AsNoTracking()
            .SingleAsync(x => x.RegistrationSessionId == registration.Id);
    
        verification.Attempts.Should().Be(RegistrationVerification.MaxAttempts);
    }
    
    [Fact]
    public async Task VerifyRegistrationCode_WhenCodeInvalid_IncrementsAttemptsAndReturnsAttemptsLeft()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var sessionToken = cryptoService.GenerateSessionToken();
    
        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "verify-invalid@example.com",
            SessionTokenHash = cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Email,
            NextStep = RegistrationStep.Code,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    
        Context.RegistrationSessions.Add(registration);
        Context.RegistrationEmailVerifications.Add(
            new RegistrationEmailVerification
            {
                RegistrationSessionId = registration.Id,
                CodeHash = cryptoService.HashCode("1234"),
                Attempts = 0,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                ResendAvailableAt = DateTime.UtcNow.AddMinutes(1)
            });
    
        await Context.SaveChangesAsync();
    
        var jwt = tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Challenge,
            registration.ExpiresAt);
    
        Client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{RegistrationRequest.CookieName}={jwt}");
    
        const string mutation = """
                                mutation ($request: VerifyRegistrationCodeRequestInput!) {
                                  verifyRegistrationCode(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code = "9999"
            }
        });
    
        var errors = doc.GetErrors();
    
        errors.Should().NotBeNull();
    
        var error = errors.Value[0];
    
        error.GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("REGISTRATION_CODE_INVALID");
    
        error.GetProperty("extensions")
            .GetProperty("attemptsLeft")
            .GetInt32()
            .Should()
            .Be(RegistrationVerification.MaxAttempts - 1);
    
        Context.ChangeTracker.Clear();
    
        var verification = await Context.RegistrationEmailVerifications.AsNoTracking()
            .SingleAsync(x => x.RegistrationSessionId == registration.Id);
    
        verification.Attempts.Should().Be(1);
    }
    
    [Fact]
    public async Task VerifyRegistrationCode_WhenLastAttemptInvalid_ReturnsAttemptsExceededWithZeroAttemptsLeft()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var sessionToken = cryptoService.GenerateSessionToken();
    
        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "verify-last-attempt@example.com",
            SessionTokenHash = cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Email,
            NextStep = RegistrationStep.Code,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    
        Context.RegistrationSessions.Add(registration);
    
        Context.RegistrationEmailVerifications.Add(
            new RegistrationEmailVerification
            {
                RegistrationSessionId = registration.Id,
                CodeHash = cryptoService.HashCode("1234"),
                Attempts = RegistrationVerification.MaxAttempts - 1,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                ResendAvailableAt = DateTime.UtcNow.AddMinutes(1)
            });
    
        await Context.SaveChangesAsync();
    
        var jwt = tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Challenge,
            registration.ExpiresAt);
    
        Client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{RegistrationRequest.CookieName}={jwt}");
    
        const string mutation = """
                                mutation ($request: VerifyRegistrationCodeRequestInput!) {
                                  verifyRegistrationCode(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code = "9999"
            }
        });
    
        var errors = doc.GetErrors();
    
        errors.Should().NotBeNull();
    
        var error = errors.Value[0];
    
        error.GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("REGISTRATION_CODE_ATTEMPTS_EXCEEDED");
    
        error.GetProperty("extensions")
            .GetProperty("attemptsLeft")
            .GetInt32()
            .Should()
            .Be(0);
    
        Context.ChangeTracker.Clear();
    
        var verification = await Context.RegistrationEmailVerifications.AsNoTracking()
            .SingleAsync(x => x.RegistrationSessionId == registration.Id);
    
        verification.Attempts.Should().Be(RegistrationVerification.MaxAttempts);
    }
    
    [Fact]
    public async Task VerifyRegistrationCode_WhenCodeValid_ConfirmsEmailAdvancesRegistrationAndRemovesVerification()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var sessionToken = cryptoService.GenerateSessionToken();
    
        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "verify-success@example.com",
            SessionTokenHash = cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Email,
            NextStep = RegistrationStep.Code,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    
        Context.RegistrationSessions.Add(registration);
    
        Context.RegistrationEmailVerifications.Add(
            new RegistrationEmailVerification
            {
                RegistrationSessionId = registration.Id,
                CodeHash = cryptoService.HashCode("1234"),
                Attempts = 0,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                ResendAvailableAt = DateTime.UtcNow.AddMinutes(1)
            });
    
        await Context.SaveChangesAsync();
    
        var jwt = tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Challenge,
            registration.ExpiresAt);
    
        Client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{RegistrationRequest.CookieName}={jwt}");
    
        const string mutation = """
                                mutation ($request: VerifyRegistrationCodeRequestInput!) {
                                  verifyRegistrationCode(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code = "1234"
            }
        });
    
        doc.GetErrors().Should().BeNull();
    
        var data = doc.GetData().GetProperty("verifyRegistrationCode");
    
        data.GetProperty("sessionToken")
            .GetString()
            .Should()
            .Be(sessionToken);
    
        Context.ChangeTracker.Clear();
    
        var storedRegistration = await Context.RegistrationSessions.AsNoTracking()
            .SingleAsync(x => x.Id == registration.Id);
    
        storedRegistration.IsEmailConfirmed.Should().BeTrue();
        storedRegistration.CurrentStep.Should().Be(RegistrationStep.Code);
        storedRegistration.NextStep.Should().Be(RegistrationStep.Password);
    
        var verificationExists = await Context.RegistrationEmailVerifications.AsNoTracking()
            .AnyAsync(x => x.RegistrationSessionId == registration.Id);
    
        verificationExists.Should().BeFalse();
    }
    
    [Fact]
    public async Task ResendRegistrationCode_WhenVerificationDoesNotExist_CreatesNewCode()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var sessionToken = cryptoService.GenerateSessionToken();
    
        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "resend-new@example.com",
            SessionTokenHash = cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Email,
            NextStep = RegistrationStep.Code,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    
        Context.RegistrationSessions.Add(registration);
        await Context.SaveChangesAsync();
    
        var jwt = tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Challenge,
            registration.ExpiresAt);
    
        Client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{RegistrationRequest.CookieName}={jwt}");
    
        const string mutation = """
                                mutation {
                                  resendRegistrationCode
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation);
    
        doc.GetErrors().Should().BeNull();
    
        doc.GetData()
            .GetProperty("resendRegistrationCode")
            .GetBoolean()
            .Should()
            .BeTrue();
    
        Context.ChangeTracker.Clear();
    
        var verification = await Context.RegistrationEmailVerifications.AsNoTracking()
            .SingleAsync(x => x.RegistrationSessionId == registration.Id);
    
        verification.Attempts.Should().Be(0);
        verification.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        verification.ResendAvailableAt.Should().BeAfter(DateTime.UtcNow);
    
        var outboxes = await Context.OutboxMessages.AsNoTracking().ToListAsync();
    
        outboxes.Should().ContainSingle(x => x.TopicKey == KafkaTopicKeys.EmailConfirmation);
    }
    
    [Fact]
    public async Task ResendRegistrationCode_WhenCooldownActive_ReturnsCooldownError()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var sessionToken = cryptoService.GenerateSessionToken();
    
        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "resend-cooldown@example.com",
            SessionTokenHash = cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Email,
            NextStep = RegistrationStep.Code,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    
        Context.RegistrationSessions.Add(registration);
    
        Context.RegistrationEmailVerifications.Add(
            new RegistrationEmailVerification
            {
                RegistrationSessionId = registration.Id,
                CodeHash = cryptoService.HashCode("1234"),
                Attempts = 0,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                ResendAvailableAt = DateTime.UtcNow.AddSeconds(30)
            });
    
        await Context.SaveChangesAsync();
    
        var jwt = tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Challenge,
            registration.ExpiresAt);
    
        Client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{RegistrationRequest.CookieName}={jwt}");
    
        const string mutation = """
                                mutation {
                                  resendRegistrationCode
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation);
    
        var errors = doc.GetErrors();
    
        errors.Should().NotBeNull();
    
        var error = errors.Value[0];
    
        error.GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("REGISTRATION_CODE_RESEND_COOLDOWN");
    
        error.GetProperty("extensions")
            .GetProperty("retryAfter")
            .GetInt32()
            .Should()
            .BeGreaterThan(0);
    
        Context.ChangeTracker.Clear();
    
        var verification = await Context.RegistrationEmailVerifications.AsNoTracking()
            .SingleAsync(x => x.RegistrationSessionId == registration.Id);
    
        verification.CodeHash.Should().Be(cryptoService.HashCode("1234"));
    
        var outboxCount = await Context.OutboxMessages.AsNoTracking().CountAsync();
    
        outboxCount.Should().Be(0);
    }
    
    [Fact]
    public async Task ResendRegistrationCode_WhenCooldownExpired_ReissuesCode()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var sessionToken = cryptoService.GenerateSessionToken();
        var oldCodeHash = cryptoService.HashCode("1234");
    
        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "resend-allowed@example.com",
            SessionTokenHash = cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Email,
            NextStep = RegistrationStep.Code,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    
        Context.RegistrationSessions.Add(registration);
    
        Context.RegistrationEmailVerifications.Add(
            new RegistrationEmailVerification
            {
                RegistrationSessionId = registration.Id,
                CodeHash = oldCodeHash,
                Attempts = 3,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5),
                ResendAvailableAt = DateTime.UtcNow.AddMinutes(-1)
            });
    
        await Context.SaveChangesAsync();
    
        var jwt = tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Challenge,
            registration.ExpiresAt);
    
        Client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{RegistrationRequest.CookieName}={jwt}");
    
        const string mutation = """
                                mutation {
                                  resendRegistrationCode
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation);
    
        doc.GetErrors().Should().BeNull();
    
        doc.GetData()
            .GetProperty("resendRegistrationCode")
            .GetBoolean()
            .Should()
            .BeTrue();
    
        Context.ChangeTracker.Clear();
    
        var verification = await Context.RegistrationEmailVerifications.AsNoTracking()
            .SingleAsync(x => x.RegistrationSessionId == registration.Id);
    
        verification.Attempts.Should().Be(0);
        verification.CodeHash.Should().NotBe(oldCodeHash);
        verification.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        verification.ResendAvailableAt.Should().BeAfter(DateTime.UtcNow);
    
        var outboxes = await Context.OutboxMessages.AsNoTracking().ToListAsync();
    
        outboxes.Should().ContainSingle(x => x.TopicKey == KafkaTopicKeys.EmailConfirmation);
    }
    
    [Fact]
    public async Task SetRegistrationPassword_Success_HashesPasswordAndAdvancesToPersonal()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var sessionToken = cryptoService.GenerateSessionToken();
    
        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "password@example.com",
            SessionTokenHash = cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Code,
            NextStep = RegistrationStep.Password,
            IsEmailConfirmed = true,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    
        Context.RegistrationSessions.Add(registration);
    
        await Context.SaveChangesAsync();
    
        var jwt = tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Authorized,
            registration.ExpiresAt);
    
        Client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{RegistrationRequest.CookieName}={jwt}");
    
        const string mutation = """
                                mutation ($request: SetRegistrationPasswordRequestInput!) {
                                  setRegistrationPassword(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;
    
        const string password = "Password1!";
    
        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                password,
                confirmPassword = password
            }
        });
    
        doc.GetErrors().Should().BeNull();
    
        var data = doc.GetData().GetProperty("setRegistrationPassword");
    
        data.GetProperty("sessionToken")
            .GetString()
            .Should()
            .Be(sessionToken);
    
        data.GetProperty("step")
            .GetString()
            .Should()
            .Be("PASSWORD");
    
        data.GetProperty("nextStep")
            .GetString()
            .Should()
            .Be("PERSONAL");
    
        Context.ChangeTracker.Clear();
    
        var storedRegistration = await Context.RegistrationSessions.AsNoTracking()
            .SingleAsync(x => x.Id == registration.Id);
    
        storedRegistration.PasswordHash.Should().NotBeNullOrWhiteSpace();
        BCrypt.Net.BCrypt.Verify(password, storedRegistration.PasswordHash).Should().BeTrue();
        storedRegistration.CurrentStep.Should().Be(RegistrationStep.Password);
        storedRegistration.NextStep.Should().Be(RegistrationStep.Personal);
    }
    
    [Fact]
    public async Task CompleteRegistration_WhenPasswordMissing_ReturnsPasswordMissingError()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var sessionToken = cryptoService.GenerateSessionToken();
    
        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "missing-password@example.com",
            SessionTokenHash = cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Password,
            NextStep = RegistrationStep.Personal,
            IsEmailConfirmed = true,
            PasswordHash = null,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    
        Context.RegistrationSessions.Add(registration);
        Context.UserConsentProjections.Add(new UserConsentProjection
        {
            UserId = UserId,
            Type = ConsentType.PersonalData,
            ConsentVersionId = Guid.NewGuid(),
            IsGranted = true,
            GrantedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow
        });
    
        await Context.SaveChangesAsync();
    
        var jwt = tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Authorized,
            registration.ExpiresAt);
    
        Client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{RegistrationRequest.CookieName}={jwt}");
    
        const string mutation = """
                                mutation ($request: SetRegistrationPersonalRequestInput!) {
                                  completeRegistration(request: $request) {
                                    accessToken
                                    refreshToken
                                    accessExpiresAtUtc
                                    refreshExpiresAtUtc
                                  }
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                name = "Name",
                surname = "Surname"
            }
        });
    
        var errors = doc.GetErrors();
    
        errors.Should().NotBeNull();
    
        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("REGISTRATION_PASSWORD_MISSING");
    
        Context.ChangeTracker.Clear();
    
        (await Context.UserCredentials.AsNoTracking().CountAsync()).Should().Be(0);
        (await Context.RegistrationSessions.AsNoTracking().AnyAsync(x => x.Id == registration.Id))
            .Should()
            .BeTrue();
    }
    
    [Fact]
    public async Task CompleteRegistration_WhenEmailAlreadyRegistered_ReturnsEmailAlreadyRegisteredError()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var sessionToken = cryptoService.GenerateSessionToken();
    
        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "duplicate-complete@example.com",
            SessionTokenHash = cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Password,
            NextStep = RegistrationStep.Personal,
            IsEmailConfirmed = true,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    
        Context.RegistrationSessions.Add(registration);
        Context.UserCredentials.Add(new UserCredential
        {
            UserId = Guid.NewGuid(),
            Email = registration.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("OtherPassword1!"),
            IsEmailConfirmed = true
        });
        Context.UserConsentProjections.Add(new UserConsentProjection
        {
            UserId = UserId,
            Type = ConsentType.PersonalData,
            ConsentVersionId = Guid.NewGuid(),
            IsGranted = true,
            GrantedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow
        });
    
        await Context.SaveChangesAsync();
    
        var jwt = tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Authorized,
            registration.ExpiresAt);
    
        Client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{RegistrationRequest.CookieName}={jwt}");
    
        const string mutation = """
                                mutation ($request: SetRegistrationPersonalRequestInput!) {
                                  completeRegistration(request: $request) {
                                    accessToken
                                    refreshToken
                                  }
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                name = "Name",
                surname = "Surname"
            }
        });
    
        var errors = doc.GetErrors();
    
        errors.Should().NotBeNull();
    
        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("EMAIL_ALREADY_REGISTERED");
    
        Context.ChangeTracker.Clear();
    
        var registrations = await Context.RegistrationSessions
            .AsNoTracking()
            .Where(x => x.Id == registration.Id)
            .ToListAsync();
    
        registrations.Should().ContainSingle();
        (await Context.RefreshTokens.AsNoTracking().CountAsync()).Should().Be(0);
    }
    
    [Fact]
    public async Task CompleteRegistration_Success_CreatesUserTransfersConsentAndReturnsTokens()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var sessionToken = cryptoService.GenerateSessionToken();
        var consentVersionId = Guid.NewGuid();
        var grantedAt = DateTime.UtcNow.AddMinutes(-10);
    
        const string password = "Password1!";
    
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "complete@example.com",
            SessionTokenHash = cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Password,
            NextStep = RegistrationStep.Personal,
            IsEmailConfirmed = true,
            PasswordHash = passwordHash,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    
        Context.RegistrationSessions.Add(registration);
        Context.UserConsentProjections.AddRange(
            new UserConsentProjection
            {
                UserId = UserId,
                Type = ConsentType.PersonalData,
                ConsentVersionId = Guid.NewGuid(),
                IsGranted = true,
                GrantedAt = DateTime.UtcNow.AddMinutes(-5),
                UpdatedAt = DateTime.UtcNow
            },
            new UserConsentProjection
            {
                UserId = registration.Id,
                Type = ConsentType.PersonalData,
                ConsentVersionId = consentVersionId,
                IsGranted = true,
                GrantedAt = grantedAt,
                UpdatedAt = DateTime.UtcNow
            });
    
        await Context.SaveChangesAsync();
    
        var jwt = tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Authorized,
            registration.ExpiresAt);
    
        Client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{RegistrationRequest.CookieName}={jwt}");
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("Planara.Tests/CompleteRegistration");
    
        const string mutation = """
                                mutation ($request: SetRegistrationPersonalRequestInput!) {
                                  completeRegistration(request: $request) {
                                    accessToken
                                    refreshToken
                                    accessExpiresAtUtc
                                    refreshExpiresAtUtc
                                  }
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                name = "  Name  ",
                surname = (string?)null
            }
        });
    
        doc.GetErrors().Should().BeNull();
    
        var data = doc.GetData().GetProperty("completeRegistration");
        var accessToken = data.GetProperty("accessToken").GetString();
        var refreshToken = data.GetProperty("refreshToken").GetString();
    
        accessToken.Should().NotBeNullOrWhiteSpace();
        refreshToken.Should().NotBeNullOrWhiteSpace();
    
        Context.ChangeTracker.Clear();
    
        var credential = await Context.UserCredentials.AsNoTracking()
            .SingleAsync(x => x.Email == "complete@example.com");
    
        credential.IsEmailConfirmed.Should().BeTrue();
        credential.PasswordHash.Should().Be(passwordHash);
    
        var userId = credential.UserId;
    
        var registrationExists = await Context.RegistrationSessions.AsNoTracking()
            .AnyAsync(x => x.Id == registration.Id);
    
        registrationExists.Should().BeFalse();
    
        var temporaryRegistrationConsentExists = await Context.UserConsentProjections.AsNoTracking()
            .AnyAsync(x => x.UserId == registration.Id);
    
        temporaryRegistrationConsentExists.Should().BeFalse();
    
        var refresh = await Context.RefreshTokens.AsNoTracking()
            .SingleAsync(x => x.UserId == userId);
    
        refresh.RevokedAtUtc.Should().BeNull();
        refresh.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
        refresh.TokenHash.Should().NotBeNullOrWhiteSpace();
        refresh.UserAgent.Should().Be("Planara.Tests/CompleteRegistration");
        tokenService.HashRefreshToken(refreshToken).Should().Be(refresh.TokenHash);
    
        var outboxes = await Context.OutboxMessages.AsNoTracking().ToListAsync();
    
        outboxes.Should().HaveCount(2);
    
        var userCreatedOutbox = outboxes.Single(x => x.TopicKey == KafkaTopicKeys.UserCreated);
        var userCreated = JsonSerializer.Deserialize<UserCreatedMessage>(userCreatedOutbox.PayloadJson, KafkaJson.SerializerOptions);
    
        userCreated.Should().NotBeNull();
        userCreated.UserId.Should().Be(userId);
        userCreated.Email.Should().Be("complete@example.com");
        userCreated.Name.Should().Be("Name");
        userCreated.Surname.Should().BeNull();
    
        var consentOutbox = outboxes.Single(x => x.TopicKey == KafkaTopicKeys.ConsentGrantRequested);
        var consent = JsonSerializer.Deserialize<ConsentGrantRequestedMessage>(consentOutbox.PayloadJson, KafkaJson.SerializerOptions);
    
        consent.Should().NotBeNull();
        consent.RegistrationId.Should().BeNull();
        consent.UserId.Should().Be(userId);
        consent.Type.Should().Be(ConsentType.PersonalData);
        consent.ConsentVersionId.Should().Be(consentVersionId);
        consent.GivenAt.Should().BeCloseTo(grantedAt, TimeSpan.FromMicroseconds(1));
    }
    
    [Fact]
    public async Task Refresh_WhenTokenDoesNotExist_ReturnsInvalidTokenError()
    {
        await ResetAsync();

        const string mutation = """
                                mutation ($request: RefreshRequestInput!) {
                                  refresh(request: $request) {
                                    accessToken
                                    refreshToken
                                  }
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                refreshToken = "not-a-real-refresh-token"
            }
        });

        var errors = doc.GetErrors();

        errors.Should().NotBeNull();

        errors.Value[0]
            .GetProperty("message")
            .GetString()
            .Should()
            .Be("Invalid refresh token");
    }
    
    [Fact]
    public async Task Refresh_WhenTokenRevoked_ReturnsRevokedError()
    {
        await ResetAsync();

        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();

        const string rawRefreshToken = "revoked-refresh-token";
        var hash = tokenService.HashRefreshToken(rawRefreshToken);

        Context.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TokenHash = hash,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(29),
            RevokedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: RefreshRequestInput!) {
                                  refresh(request: $request) {
                                    accessToken
                                    refreshToken
                                  }
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                refreshToken = rawRefreshToken
            }
        });

        var errors = doc.GetErrors();

        errors.Should().NotBeNull();

        errors.Value[0]
            .GetProperty("message")
            .GetString()
            .Should()
            .Be("Refresh token revoked");

        Context.ChangeTracker.Clear();

        var stored = await Context.RefreshTokens.AsNoTracking().SingleAsync();

        stored.ReplacedByTokenHash.Should().BeNull();
    }
    
    [Fact]
    public async Task Refresh_WhenTokenExpired_ReturnsExpiredError()
    {
        await ResetAsync();

        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();

        const string rawRefreshToken = "expired-refresh-token";
        var hash = tokenService.HashRefreshToken(rawRefreshToken);

        Context.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TokenHash = hash,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-31),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1),
            RevokedAtUtc = null
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: RefreshRequestInput!) {
                                  refresh(request: $request) {
                                    accessToken
                                    refreshToken
                                  }
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                refreshToken = rawRefreshToken
            }
        });

        var errors = doc.GetErrors();

        errors.Should().NotBeNull();

        errors.Value[0]
            .GetProperty("message")
            .GetString()
            .Should()
            .Be("Refresh token expired");

        Context.ChangeTracker.Clear();

        (await Context.RefreshTokens.AsNoTracking().CountAsync()).Should().Be(1);
    }
    
    [Fact]
    public async Task Refresh_Success_RotatesRefreshTokenAndReturnsNewTokens()
    {
        await ResetAsync();
    
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var userId = Guid.NewGuid();
    
        const string oldRawRefreshToken = "valid-refresh-token";
        var oldHash = tokenService.HashRefreshToken(oldRawRefreshToken);
    
        Context.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = oldHash,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(29),
            RevokedAtUtc = null
        });
    
        await Context.SaveChangesAsync();
    
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("Planara.Tests/Refresh");
    
        const string mutation = """
                                mutation ($request: RefreshRequestInput!) {
                                  refresh(request: $request) {
                                    accessToken
                                    refreshToken
                                    accessExpiresAtUtc
                                    refreshExpiresAtUtc
                                  }
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                refreshToken = oldRawRefreshToken
            }
        });
    
        doc.GetErrors().Should().BeNull();
    
        var data = doc.GetData().GetProperty("refresh");
        var accessToken = data.GetProperty("accessToken").GetString();
        var newRawRefreshToken = data.GetProperty("refreshToken").GetString();
    
        accessToken.Should().NotBeNullOrWhiteSpace();
        newRawRefreshToken.Should().NotBeNullOrWhiteSpace();
        newRawRefreshToken.Should().NotBe(oldRawRefreshToken);
    
        Context.ChangeTracker.Clear();
    
        var tokens = await Context.RefreshTokens
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();
    
        tokens.Should().HaveCount(2);
    
        var oldToken = tokens.Single(x => x.TokenHash == oldHash);
    
        oldToken.RevokedAtUtc.Should().NotBeNull();
        oldToken.ReplacedByTokenHash.Should().NotBeNullOrWhiteSpace();
    
        var newToken = tokens.Single(x => x.TokenHash != oldHash);
    
        newToken.RevokedAtUtc.Should().BeNull();
        newToken.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
        newToken.UserAgent.Should().Be("Planara.Tests/Refresh");
        tokenService.HashRefreshToken(newRawRefreshToken).Should().Be(newToken.TokenHash);
        oldToken.ReplacedByTokenHash.Should().Be(newToken.TokenHash);
    }
    
    [Fact]
    public async Task Logout_WhenTokenDoesNotExist_ReturnsSuccess()
    {
        await ResetAsync();

        const string mutation = """
                                mutation ($request: LogoutRequestInput!) {
                                  logout(request: $request) {
                                    success
                                  }
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                refreshToken = "missing-refresh-token"
            }
        });

        doc.GetErrors().Should().BeNull();

        doc.GetData()
            .GetProperty("logout")
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue();

        (await Context.RefreshTokens.AsNoTracking().CountAsync()).Should().Be(0);
    }
    
    [Fact]
    public async Task Logout_WhenTokenAlreadyRevoked_ReturnsSuccessAndDoesNotChangeRevokedAt()
    {
        await ResetAsync();

        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        const string rawRefreshToken = "already-revoked-refresh-token";
        var hash = tokenService.HashRefreshToken(rawRefreshToken);
        var revokedAt = DateTime.UtcNow.AddMinutes(-10);

        Context.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TokenHash = hash,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(29),
            RevokedAtUtc = revokedAt
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: LogoutRequestInput!) {
                                  logout(request: $request) {
                                    success
                                  }
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                refreshToken = rawRefreshToken
            }
        });

        doc.GetErrors().Should().BeNull();

        doc.GetData()
            .GetProperty("logout")
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue();

        Context.ChangeTracker.Clear();

        var stored = await Context.RefreshTokens.AsNoTracking().SingleAsync();

        stored.RevokedAtUtc.Should().Be(revokedAt);
    }
    
    [Fact]
    public async Task Logout_WhenTokenActive_RevokesTokenAndReturnsSuccess()
    {
        await ResetAsync();

        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();

        const string rawRefreshToken = "active-refresh-token";
        var hash = tokenService.HashRefreshToken(rawRefreshToken);

        Context.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TokenHash = hash,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(29),
            RevokedAtUtc = null
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: LogoutRequestInput!) {
                                  logout(request: $request) {
                                    success
                                  }
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                refreshToken = rawRefreshToken
            }
        });

        doc.GetErrors().Should().BeNull();

        doc.GetData()
            .GetProperty("logout")
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue();

        Context.ChangeTracker.Clear();

        var stored = await Context.RefreshTokens.AsNoTracking().SingleAsync();

        stored.RevokedAtUtc.Should().NotBeNull();
        stored.RevokedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
    
    [Fact]
    public async Task ChangePassword_WhenCurrentPasswordInvalid_ReturnsInvalidCurrentPasswordError()
    {
        await ResetAsync();

        const string currentPassword = "Password1!";

        var originalHash = BCrypt.Net.BCrypt.HashPassword(currentPassword, workFactor: 12);

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "change-password@example.com",
            PasswordHash = originalHash,
            IsEmailConfirmed = true
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ChangePasswordRequestInput!) {
                                  changePassword(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                currentPassword = "WrongPassword1!",
                newPassword = "NewPassword1!",
                confirmPassword = "NewPassword1!"
            }
        });

        var errors = doc.GetErrors();

        errors.Should().NotBeNull();

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("INVALID_CURRENT_PASSWORD");

        Context.ChangeTracker.Clear();

        var credential = await Context.UserCredentials.AsNoTracking().SingleAsync(x => x.UserId == UserId);

        credential.PasswordHash.Should().Be(originalHash);
    }
    
    [Fact]
    public async Task ChangePassword_WhenNewPasswordEqualsCurrent_ReturnsPasswordNotChangedError()
    {
        await ResetAsync();

        const string currentPassword = "Password1!";

        var originalHash = BCrypt.Net.BCrypt.HashPassword(currentPassword, workFactor: 12);

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "same-password@example.com",
            PasswordHash = originalHash,
            IsEmailConfirmed = true
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ChangePasswordRequestInput!) {
                                  changePassword(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                currentPassword,
                newPassword = currentPassword,
                confirmPassword = currentPassword
            }
        });

        var errors = doc.GetErrors();

        errors.Should().NotBeNull();

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("PASSWORD_NOT_CHANGED");

        Context.ChangeTracker.Clear();

        var credential = await Context.UserCredentials.AsNoTracking().SingleAsync(x => x.UserId == UserId);

        credential.PasswordHash.Should().Be(originalHash);
    }
    
    [Fact]
    public async Task ChangePassword_Success_UpdatesPasswordHash()
    {
        await ResetAsync();

        const string currentPassword = "Password1!";
        const string newPassword = "NewPassword1!";

        var originalHash = BCrypt.Net.BCrypt.HashPassword(currentPassword, workFactor: 12);

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "successful-password-change@example.com",
            PasswordHash = originalHash,
            IsEmailConfirmed = true
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ChangePasswordRequestInput!) {
                                  changePassword(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                currentPassword,
                newPassword,
                confirmPassword = newPassword
            }
        });

        doc.GetErrors().Should().BeNull();

        doc.GetData()
            .GetProperty("changePassword")
            .GetBoolean()
            .Should()
            .BeTrue();

        Context.ChangeTracker.Clear();

        var credential = await Context.UserCredentials.AsNoTracking()
            .SingleAsync(x => x.UserId == UserId);

        credential.PasswordHash.Should().NotBe(originalHash);
        BCrypt.Net.BCrypt.Verify(newPassword, credential.PasswordHash).Should().BeTrue();
        BCrypt.Net.BCrypt.Verify(currentPassword, credential.PasswordHash).Should().BeFalse();
    }
    
    [Fact]
    public async Task ChangeEmail_WhenEmailNotChanged_ReturnsEmailNotChangedError()
    {
        await ResetAsync();

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "same@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = true
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ChangeEmailRequestInput!) {
                                  changeEmail(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                email = "  SAME@EXAMPLE.COM  "
            }
        });

        var errors = doc.GetErrors();

        errors.Should().NotBeNull();

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("EMAIL_NOT_CHANGED");

        Context.ChangeTracker.Clear();

        (await Context.UserEmailVerifications.AsNoTracking().CountAsync()).Should().Be(0);
        (await Context.OutboxMessages.AsNoTracking().CountAsync()).Should().Be(0);
    }
    
    [Fact]
    public async Task ChangeEmail_WhenEmailAlreadyRegistered_ReturnsEmailAlreadyRegisteredError()
    {
        await ResetAsync();

        Context.UserCredentials.AddRange(
            new UserCredential
            {
                UserId = UserId,
                Email = "current@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
                IsEmailConfirmed = true
            },
            new UserCredential
            {
                UserId = Guid.NewGuid(),
                Email = "taken@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password2!"),
                IsEmailConfirmed = true
            });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ChangeEmailRequestInput!) {
                                  changeEmail(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                email = "TAKEN@EXAMPLE.COM"
            }
        });

        var errors = doc.GetErrors();

        errors.Should().NotBeNull();

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("EMAIL_ALREADY_REGISTERED");

        Context.ChangeTracker.Clear();

        (await Context.UserEmailVerifications.AsNoTracking().CountAsync()).Should().Be(0);
        (await Context.OutboxMessages.AsNoTracking().CountAsync()).Should().Be(0);
    }
    
    [Fact]
    public async Task ChangeEmail_WhenVerificationDoesNotExist_CreatesVerificationAndOutbox()
    {
        await ResetAsync();
    
        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "current@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = true
        });
    
        await Context.SaveChangesAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
    
        const string mutation = """
                                mutation ($request: ChangeEmailRequestInput!) {
                                  changeEmail(request: $request)
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                email = "  NEW@EXAMPLE.COM "
            }
        });
    
        doc.GetErrors().Should().BeNull();
    
        doc.GetData()
            .GetProperty("changeEmail")
            .GetBoolean()
            .Should()
            .BeTrue();
    
        Context.ChangeTracker.Clear();
    
        var credential = await Context.UserCredentials.AsNoTracking()
            .SingleAsync(x => x.UserId == UserId);
    
        credential.Email.Should().Be("current@example.com");
    
        var verification = await Context.UserEmailVerifications.AsNoTracking()
            .SingleAsync();
    
        verification.UserId.Should().Be(UserId);
        verification.Type.Should().Be(EmailVerificationType.EmailChange);
        verification.Email.Should().Be("new@example.com");
        verification.Attempts.Should().Be(0);
        verification.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        verification.ResendAvailableAt.Should().BeAfter(DateTime.UtcNow);
        verification.CodeHash.Should().NotBeNullOrWhiteSpace();
    
        var outbox = await Context.OutboxMessages.AsNoTracking().SingleAsync();
    
        outbox.TopicKey.Should().Be(KafkaTopicKeys.EmailConfirmation);
        outbox.Type.Should().Be(nameof(EmailConfirmationMessage));
        outbox.Key.Should().Be(UserId.ToString("N"));
    
        var message = JsonSerializer.Deserialize<EmailConfirmationMessage>(outbox.PayloadJson, KafkaJson.SerializerOptions);
    
        message.Should().NotBeNull();
        message.Email.Should().Be("new@example.com");
        message.Code.Should().NotBeNullOrWhiteSpace();
        cryptoService.VerifyCode(message.Code, verification.CodeHash).Should().BeTrue();
    }
    
    [Fact]
    public async Task ChangeEmail_WhenVerificationExists_UpdatesVerificationAndCreatesOutbox()
    {
        await ResetAsync();
    
        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "current@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = true
        });
    
        Context.UserEmailVerifications.Add(new UserEmailVerification
        {
            UserId = UserId,
            Type = EmailVerificationType.EmailChange,
            Email = "old-pending@example.com",
            CodeHash = "old-code-hash",
            Attempts = 3,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10),
            ResendAvailableAt = DateTime.UtcNow.AddMinutes(-5)
        });
    
        await Context.SaveChangesAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
    
        const string mutation = """
                                mutation ($request: ChangeEmailRequestInput!) {
                                  changeEmail(request: $request)
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                email = "updated@example.com"
            }
        });
    
        doc.GetErrors().Should().BeNull();
    
        doc.GetData()
            .GetProperty("changeEmail")
            .GetBoolean()
            .Should()
            .BeTrue();
    
        Context.ChangeTracker.Clear();
    
        var verification = await Context.UserEmailVerifications.AsNoTracking().SingleAsync();
    
        verification.UserId.Should().Be(UserId);
        verification.Type.Should().Be(EmailVerificationType.EmailChange);
        verification.Email.Should().Be("updated@example.com");
        verification.CodeHash.Should().NotBe("old-code-hash");
        verification.Attempts.Should().Be(0);
        verification.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        verification.ResendAvailableAt.Should().BeAfter(DateTime.UtcNow);
    
        var outbox = await Context.OutboxMessages.AsNoTracking().SingleAsync();
    
        outbox.TopicKey.Should().Be(KafkaTopicKeys.EmailConfirmation);
        outbox.Type.Should().Be(nameof(EmailConfirmationMessage));
        outbox.Key.Should().Be(UserId.ToString("N"));
    
        var message = JsonSerializer.Deserialize<EmailConfirmationMessage>(outbox.PayloadJson, KafkaJson.SerializerOptions);
    
        message.Should().NotBeNull();
        message.Email.Should().Be("updated@example.com");
        cryptoService.VerifyCode(message.Code, verification.CodeHash).Should().BeTrue();
    }
    
    [Fact]
    public async Task ConfirmEmailChange_WhenVerificationDoesNotExist_ReturnsNotFoundError()
    {
        await ResetAsync();

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "current@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = true
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ConfirmEmailChangeRequestInput!) {
                                  confirmEmailChange(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code = "1234"
            }
        });

        var errors = doc.GetErrors();

        errors.Should().NotBeNull();

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("EMAIL_CHANGE_NOT_FOUND");
    }
    
    [Fact]
    public async Task ConfirmEmailChange_WhenCodeExpired_ReturnsExpiredError()
    {
        await ResetAsync();

        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "current@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = true
        });

        Context.UserEmailVerifications.Add(new UserEmailVerification
        {
            UserId = UserId,
            Type = EmailVerificationType.EmailChange,
            Email = "new@example.com",
            CodeHash = cryptoService.HashCode("1234"),
            Attempts = 0,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            ResendAvailableAt = DateTime.UtcNow.AddMinutes(-2)
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ConfirmEmailChangeRequestInput!) {
                                  confirmEmailChange(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code = "1234"
            }
        });

        var errors = doc.GetErrors();

        errors.Should().NotBeNull();

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("EMAIL_CHANGE_CODE_EXPIRED");
    }
    
    [Fact]
    public async Task ConfirmEmailChange_WhenAttemptsExceeded_ReturnsAttemptsExceededError()
    {
        await ResetAsync();

        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "current@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = true
        });

        Context.UserEmailVerifications.Add(new UserEmailVerification
        {
            UserId = UserId,
            Type = EmailVerificationType.EmailChange,
            Email = "new@example.com",
            CodeHash = cryptoService.HashCode("1234"),
            Attempts = RegistrationVerification.MaxAttempts,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            ResendAvailableAt = DateTime.UtcNow
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ConfirmEmailChangeRequestInput!) {
                                  confirmEmailChange(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code = "1234"
            }
        });

        var errors = doc.GetErrors();

        errors.Should().NotBeNull();

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("EMAIL_CHANGE_CODE_ATTEMPTS_EXCEEDED");
    }
    
    [Fact]
    public async Task ConfirmEmailChange_WhenCodeInvalid_IncrementsAttemptsAndReturnsAttemptsLeft()
    {
        await ResetAsync();

        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "current@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = true
        });

        Context.UserEmailVerifications.Add(new UserEmailVerification
        {
            UserId = UserId,
            Type = EmailVerificationType.EmailChange,
            Email = "new@example.com",
            CodeHash = cryptoService.HashCode("1234"),
            Attempts = 0,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            ResendAvailableAt = DateTime.UtcNow
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ConfirmEmailChangeRequestInput!) {
                                  confirmEmailChange(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code = "9999"
            }
        });

        var errors = doc.GetErrors();

        errors.Should().NotBeNull();

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("EMAIL_CHANGE_CODE_INVALID");

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("attemptsLeft")
            .GetInt32()
            .Should()
            .Be(RegistrationVerification.MaxAttempts - 1);

        Context.ChangeTracker.Clear();

        var verification = await Context.UserEmailVerifications.AsNoTracking().SingleAsync();

        verification.Attempts.Should().Be(1);
    }
    
    [Fact]
    public async Task ConfirmEmailChange_WhenLastAttemptInvalid_ReturnsAttemptsExceededError()
    {
        await ResetAsync();

        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "current@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = true
        });

        Context.UserEmailVerifications.Add(new UserEmailVerification
        {
            UserId = UserId,
            Type = EmailVerificationType.EmailChange,
            Email = "new@example.com",
            CodeHash = cryptoService.HashCode("1234"),
            Attempts = RegistrationVerification.MaxAttempts - 1,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            ResendAvailableAt = DateTime.UtcNow
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ConfirmEmailChangeRequestInput!) {
                                  confirmEmailChange(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code = "9999"
            }
        });

        var errors = doc.GetErrors();

        errors.Should().NotBeNull();

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("EMAIL_CHANGE_CODE_ATTEMPTS_EXCEEDED");

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("attemptsLeft")
            .GetInt32()
            .Should()
            .Be(0);

        Context.ChangeTracker.Clear();

        var verification = await Context.UserEmailVerifications.AsNoTracking().SingleAsync();

        verification.Attempts.Should().Be(RegistrationVerification.MaxAttempts);
    }
    
    [Fact]
    public async Task ConfirmEmailChange_WhenEmailAlreadyRegistered_ReturnsEmailAlreadyRegisteredError()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
    
        const string code = "1234";
        const string newEmail = "taken@example.com";
    
        Context.UserCredentials.AddRange(
            new UserCredential
            {
                UserId = UserId,
                Email = "current@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
                IsEmailConfirmed = true
            },
            new UserCredential
            {
                UserId = Guid.NewGuid(),
                Email = newEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password2!"),
                IsEmailConfirmed = true
            });
    
        Context.UserEmailVerifications.Add(new UserEmailVerification
        {
            UserId = UserId,
            Type = EmailVerificationType.EmailChange,
            Email = newEmail,
            CodeHash = cryptoService.HashCode(code),
            Attempts = 0,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            ResendAvailableAt = DateTime.UtcNow
        });
    
        await Context.SaveChangesAsync();
    
        const string mutation = """
                                mutation ($request: ConfirmEmailChangeRequestInput!) {
                                  confirmEmailChange(request: $request)
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code
            }
        });
    
        var errors = doc.GetErrors();
    
        errors.Should().NotBeNull();
    
        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("EMAIL_ALREADY_REGISTERED");
    
        Context.ChangeTracker.Clear();
    
        var credential = await Context.UserCredentials.AsNoTracking().SingleAsync(x => x.UserId == UserId);
    
        credential.Email.Should().Be("current@example.com");
        (await Context.UserEmailVerifications.AsNoTracking().CountAsync()).Should().Be(1);
    }
    
    [Fact]
    public async Task ConfirmEmailChange_Success_UpdatesEmailAndRemovesVerification()
    {
        await ResetAsync();

        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();

        const string code = "1234";
        const string newEmail = "new@example.com";

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "current@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = false
        });

        Context.UserEmailVerifications.Add(new UserEmailVerification
        {
            UserId = UserId,
            Type = EmailVerificationType.EmailChange,
            Email = newEmail,
            CodeHash = cryptoService.HashCode(code),
            Attempts = 0,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            ResendAvailableAt = DateTime.UtcNow
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ConfirmEmailChangeRequestInput!) {
                                  confirmEmailChange(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code
            }
        });

        doc.GetErrors().Should().BeNull();

        doc.GetData()
            .GetProperty("confirmEmailChange")
            .GetBoolean()
            .Should()
            .BeTrue();

        Context.ChangeTracker.Clear();

        var credential = await Context.UserCredentials.AsNoTracking().SingleAsync(x => x.UserId == UserId);

        credential.Email.Should().Be(newEmail);
        credential.IsEmailConfirmed.Should().BeTrue();
        (await Context.UserEmailVerifications.AsNoTracking().CountAsync()).Should().Be(0);
    }
    
    [Fact]
    public async Task ChangeRegistrationEmail_WhenEmailNotChanged_ReturnsCurrentFlow()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var sessionToken = cryptoService.GenerateSessionToken();
    
        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "current@example.com",
            SessionTokenHash = cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Email,
            NextStep = RegistrationStep.Code,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    
        Context.RegistrationSessions.Add(registration);
        await Context.SaveChangesAsync();
    
        var jwt = tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Challenge,
            registration.ExpiresAt);
    
        Client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{RegistrationRequest.CookieName}={jwt}");
    
        const string mutation = """
                                mutation ($request: ChangeRegistrationEmailRequestInput!) {
                                  changeRegistrationEmail(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                email = "  CURRENT@EXAMPLE.COM  "
            }
        });
    
        doc.GetErrors().Should().BeNull();
    
        var data = doc.GetData().GetProperty("changeRegistrationEmail");
    
        data.GetProperty("sessionToken")
            .GetString()
            .Should()
            .Be(sessionToken);
    
        Context.ChangeTracker.Clear();
    
        var stored = await Context.RegistrationSessions.AsNoTracking().SingleAsync(x => x.Id == registration.Id);
    
        stored.Email.Should().Be("current@example.com");
    
        (await Context.RegistrationEmailVerifications.AsNoTracking().CountAsync()).Should().Be(0);
    }
    
    [Fact]
    public async Task ChangeRegistrationEmail_WhenEmailAlreadyRegistered_ReturnsEmailAlreadyRegisteredError()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var sessionToken = cryptoService.GenerateSessionToken();
    
        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "current@example.com",
            SessionTokenHash = cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Email,
            NextStep = RegistrationStep.Code,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    
        Context.RegistrationSessions.Add(registration);
    
        Context.UserCredentials.Add(new UserCredential
        {
            UserId = Guid.NewGuid(),
            Email = "taken@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = true
        });
    
        await Context.SaveChangesAsync();
    
        var jwt = tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Challenge,
            registration.ExpiresAt);
    
        Client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{RegistrationRequest.CookieName}={jwt}");
    
        const string mutation = """
                                mutation ($request: ChangeRegistrationEmailRequestInput!) {
                                  changeRegistrationEmail(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                email = "TAKEN@EXAMPLE.COM"
            }
        });
    
        var errors = doc.GetErrors();
    
        errors.Should().NotBeNull();
    
        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("EMAIL_ALREADY_REGISTERED");
    
        Context.ChangeTracker.Clear();
    
        var stored = await Context.RegistrationSessions.AsNoTracking().SingleAsync(x => x.Id == registration.Id);
    
        stored.Email.Should().Be("current@example.com");
    }
    
    [Fact]
    public async Task ChangeRegistrationEmail_WhenAnotherActiveRegistrationUsesEmail_ReturnsEmailAlreadyInUseError()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var sessionToken = cryptoService.GenerateSessionToken();
    
        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "current@example.com",
            SessionTokenHash = cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Email,
            NextStep = RegistrationStep.Code,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    
        var otherRegistration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "pending@example.com",
            SessionTokenHash = cryptoService.HashSessionToken(cryptoService.GenerateSessionToken()),
            CurrentStep = RegistrationStep.Email,
            NextStep = RegistrationStep.Code,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    
        Context.RegistrationSessions.AddRange(registration, otherRegistration);
    
        await Context.SaveChangesAsync();
    
        var jwt = tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Challenge,
            registration.ExpiresAt);
    
        Client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{RegistrationRequest.CookieName}={jwt}");
    
        const string mutation = """
                                mutation ($request: ChangeRegistrationEmailRequestInput!) {
                                  changeRegistrationEmail(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                email = "pending@example.com"
            }
        });
    
        var errors = doc.GetErrors();
    
        errors.Should().NotBeNull();
    
        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("REGISTRATION_EMAIL_ALREADY_IN_USE");
    
        Context.ChangeTracker.Clear();
    
        var stored = await Context.RegistrationSessions.AsNoTracking().SingleAsync(x => x.Id == registration.Id);
    
        stored.Email.Should().Be("current@example.com");
    }
    
    [Fact]
    public async Task ChangeRegistrationEmail_Success_UpdatesEmailAndIssuesNewCode()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var sessionToken = cryptoService.GenerateSessionToken();
    
        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "current@example.com",
            SessionTokenHash = cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Email,
            NextStep = RegistrationStep.Code,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    
        Context.RegistrationSessions.Add(registration);
        await Context.SaveChangesAsync();
    
        var jwt = tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Challenge,
            registration.ExpiresAt);
    
        Client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{RegistrationRequest.CookieName}={jwt}");
    
        const string mutation = """
                                mutation ($request: ChangeRegistrationEmailRequestInput!) {
                                  changeRegistrationEmail(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                email = "  NEW@EXAMPLE.COM  "
            }
        });
    
        doc.GetErrors().Should().BeNull();
    
        var data = doc.GetData().GetProperty("changeRegistrationEmail");
    
        data.GetProperty("sessionToken")
            .GetString()
            .Should()
            .Be(sessionToken);
    
        Context.ChangeTracker.Clear();
    
        var stored = await Context.RegistrationSessions.AsNoTracking().SingleAsync(x => x.Id == registration.Id);
    
        stored.Email.Should().Be("new@example.com");
    
        var verification = await Context.RegistrationEmailVerifications.AsNoTracking().SingleAsync(x => x.RegistrationSessionId == registration.Id);
    
        verification.Attempts.Should().Be(0);
        verification.CodeHash.Should().NotBeNullOrWhiteSpace();
        verification.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        verification.ResendAvailableAt.Should().BeAfter(DateTime.UtcNow);
    
        var outbox = await Context.OutboxMessages.AsNoTracking().SingleAsync(x => x.TopicKey == KafkaTopicKeys.EmailConfirmation);
        var message = JsonSerializer.Deserialize<EmailConfirmationMessage>(outbox.PayloadJson, KafkaJson.SerializerOptions);
    
        message.Should().NotBeNull();
        message.Email.Should().Be("new@example.com");
        cryptoService.VerifyCode(message.Code, verification.CodeHash).Should().BeTrue();
    }
    
    [Fact]
    public async Task RequestEmailConfirmation_WhenEmailAlreadyConfirmed_ReturnsTrue()
    {
        await ResetAsync();

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "confirmed@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = true
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation {
                                  requestEmailConfirmation
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new { });

        doc.GetErrors().Should().BeNull();

        doc.GetData()
            .GetProperty("requestEmailConfirmation")
            .GetBoolean()
            .Should()
            .BeTrue();

        Context.ChangeTracker.Clear();

        (await Context.UserEmailVerifications.AsNoTracking().CountAsync()).Should().Be(0);
        (await Context.OutboxMessages.AsNoTracking().CountAsync()).Should().Be(0);
    }
    
    [Fact]
    public async Task RequestEmailConfirmation_WhenResendCooldownActive_ReturnsCooldownError()
    {
        await ResetAsync();

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "cooldown@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = false
        });

        Context.UserEmailVerifications.Add(new UserEmailVerification
        {
            UserId = UserId,
            Type = EmailVerificationType.EmailConfirmation,
            Email = "cooldown@example.com",
            CodeHash = "old-code-hash",
            Attempts = 0,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            ResendAvailableAt = DateTime.UtcNow.AddMinutes(1)
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation {
                                  requestEmailConfirmation
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new { });

        var errors = doc.GetErrors();

        errors.Should().NotBeNull();

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("EMAIL_CONFIRMATION_RESEND_COOLDOWN");

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("retryAfter")
            .GetInt32()
            .Should()
            .BeGreaterThan(0);

        Context.ChangeTracker.Clear();

        var verification = await Context.UserEmailVerifications.AsNoTracking().SingleAsync();

        verification.CodeHash.Should().Be("old-code-hash");

        (await Context.OutboxMessages.AsNoTracking().CountAsync()).Should().Be(0);
    }
    
    [Fact]
    public async Task RequestEmailConfirmation_WhenVerificationDoesNotExist_CreatesVerificationAndOutbox()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
    
        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "new-confirmation@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = false
        });
    
        await Context.SaveChangesAsync();
    
        const string mutation = """
                                mutation {
                                  requestEmailConfirmation
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation, new { });
    
        doc.GetErrors().Should().BeNull();
    
        doc.GetData()
            .GetProperty("requestEmailConfirmation")
            .GetBoolean()
            .Should()
            .BeTrue();
    
        Context.ChangeTracker.Clear();
    
        var verification = await Context.UserEmailVerifications.AsNoTracking().SingleAsync();
    
        verification.UserId.Should().Be(UserId);
        verification.Type.Should().Be(EmailVerificationType.EmailConfirmation);
        verification.Email.Should().Be("new-confirmation@example.com");
        verification.Attempts.Should().Be(0);
        verification.CodeHash.Should().NotBeNullOrWhiteSpace();
        verification.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        verification.ResendAvailableAt.Should().BeAfter(DateTime.UtcNow);
    
        var outbox = await Context.OutboxMessages.AsNoTracking().SingleAsync();
    
        outbox.TopicKey.Should().Be(KafkaTopicKeys.EmailConfirmation);
        outbox.Type.Should().Be(nameof(EmailConfirmationMessage));
        outbox.Key.Should().Be(UserId.ToString("N"));
    
        var message = JsonSerializer.Deserialize<EmailConfirmationMessage>(outbox.PayloadJson, KafkaJson.SerializerOptions);
    
        message.Should().NotBeNull();
        message.Email.Should().Be("new-confirmation@example.com");
        message.Code.Should().NotBeNullOrWhiteSpace();
    
        cryptoService.VerifyCode(message.Code, verification.CodeHash).Should().BeTrue();
    }
    
    [Fact]
    public async Task RequestEmailConfirmation_WhenVerificationExistsAndCooldownExpired_UpdatesVerificationAndCreatesOutbox()
    {
        await ResetAsync();
    
        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
    
        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "updated-confirmation@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = false
        });
    
        Context.UserEmailVerifications.Add(new UserEmailVerification
        {
            UserId = UserId,
            Type = EmailVerificationType.EmailConfirmation,
            Email = "old@example.com",
            CodeHash = "old-code-hash",
            Attempts = 3,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
            ResendAvailableAt = DateTime.UtcNow.AddMinutes(-1)
        });
    
        await Context.SaveChangesAsync();
    
        const string mutation = """
                                mutation {
                                  requestEmailConfirmation
                                }
                                """;
    
        using var doc = await Client.PostAsync(mutation, new { });
    
        doc.GetErrors().Should().BeNull();
    
        doc.GetData()
            .GetProperty("requestEmailConfirmation")
            .GetBoolean()
            .Should()
            .BeTrue();
    
        Context.ChangeTracker.Clear();
    
        var verification = await Context.UserEmailVerifications.AsNoTracking().SingleAsync();
    
        verification.UserId.Should().Be(UserId);
        verification.Type.Should().Be(EmailVerificationType.EmailConfirmation);
        verification.Email.Should().Be("updated-confirmation@example.com");
        verification.CodeHash.Should().NotBe("old-code-hash");
        verification.Attempts.Should().Be(0);
        verification.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        verification.ResendAvailableAt.Should().BeAfter(DateTime.UtcNow);
    
        var outbox = await Context.OutboxMessages.AsNoTracking().SingleAsync();
    
        outbox.TopicKey.Should().Be(KafkaTopicKeys.EmailConfirmation);
        outbox.Type.Should().Be(nameof(EmailConfirmationMessage));
        outbox.Key.Should().Be(UserId.ToString("N"));
    
        var message = JsonSerializer.Deserialize<EmailConfirmationMessage>(outbox.PayloadJson, KafkaJson.SerializerOptions);
    
        message.Should().NotBeNull();
        message.Email.Should().Be("updated-confirmation@example.com");
        cryptoService.VerifyCode(message.Code, verification.CodeHash).Should().BeTrue();
    }
    
    [Fact]
    public async Task ConfirmEmail_WhenAlreadyConfirmed_ReturnsTrue()
    {
        await ResetAsync();

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "confirmed@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = true
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ConfirmEmailRequestInput!) {
                                  confirmEmail(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code = "1234"
            }
        });

        doc.GetErrors().Should().BeNull();

        doc.GetData()
            .GetProperty("confirmEmail")
            .GetBoolean()
            .Should()
            .BeTrue();
    }
    
    [Fact]
    public async Task ConfirmEmail_WhenVerificationDoesNotExist_ReturnsNotFoundError()
    {
        await ResetAsync();

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "not-found@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = false
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ConfirmEmailRequestInput!) {
                                  confirmEmail(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code = "1234"
            }
        });

        var errors = doc.GetErrors();

        errors.Should().NotBeNull();

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("EMAIL_CONFIRMATION_NOT_FOUND");
    }
    
    [Fact]
    public async Task ConfirmEmail_WhenCodeExpired_ReturnsExpiredError()
    {
        await ResetAsync();

        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "expired@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = false
        });

        Context.UserEmailVerifications.Add(new UserEmailVerification
        {
            UserId = UserId,
            Type = EmailVerificationType.EmailConfirmation,
            Email = "expired@example.com",
            CodeHash = cryptoService.HashCode("1234"),
            Attempts = 0,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            ResendAvailableAt = DateTime.UtcNow.AddMinutes(-2)
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ConfirmEmailRequestInput!) {
                                  confirmEmail(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code = "1234"
            }
        });

        var errors = doc.GetErrors();

        errors.Should().NotBeNull();

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("EMAIL_CONFIRMATION_CODE_EXPIRED");
    }
    
    [Fact]
    public async Task ConfirmEmail_WhenAttemptsExceeded_ReturnsAttemptsExceededError()
    {
        await ResetAsync();

        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "attempts@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = false
        });

        Context.UserEmailVerifications.Add(new UserEmailVerification
        {
            UserId = UserId,
            Type = EmailVerificationType.EmailConfirmation,
            Email = "attempts@example.com",
            CodeHash = cryptoService.HashCode("1234"),
            Attempts = RegistrationVerification.MaxAttempts,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            ResendAvailableAt = DateTime.UtcNow
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ConfirmEmailRequestInput!) {
                                  confirmEmail(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code = "1234"
            }
        });

        var errors = doc.GetErrors();

        errors.Should().NotBeNull();

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("EMAIL_CONFIRMATION_CODE_ATTEMPTS_EXCEEDED");
    }
    
    [Fact]
    public async Task ConfirmEmail_WhenCodeInvalid_IncrementsAttemptsAndReturnsAttemptsLeft()
    {
        await ResetAsync();

        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "invalid@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = false
        });

        Context.UserEmailVerifications.Add(new UserEmailVerification
        {
            UserId = UserId,
            Type = EmailVerificationType.EmailConfirmation,
            Email = "invalid@example.com",
            CodeHash = cryptoService.HashCode("1234"),
            Attempts = 0,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            ResendAvailableAt = DateTime.UtcNow
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ConfirmEmailRequestInput!) {
                                  confirmEmail(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code = "9999"
            }
        });

        var errors = doc.GetErrors();

        errors.Should().NotBeNull();

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("EMAIL_CONFIRMATION_CODE_INVALID");

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("attemptsLeft")
            .GetInt32()
            .Should()
            .Be(RegistrationVerification.MaxAttempts - 1);

        Context.ChangeTracker.Clear();

        var verification = await Context.UserEmailVerifications.AsNoTracking().SingleAsync();

        verification.Attempts.Should().Be(1);
    }
    
    [Fact]
    public async Task ConfirmEmail_WhenLastAttemptInvalid_ReturnsAttemptsExceededError()
    {
        await ResetAsync();

        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "last-attempt@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = false
        });

        Context.UserEmailVerifications.Add(new UserEmailVerification
        {
            UserId = UserId,
            Type = EmailVerificationType.EmailConfirmation,
            Email = "last-attempt@example.com",
            CodeHash = cryptoService.HashCode("1234"),
            Attempts = RegistrationVerification.MaxAttempts - 1,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            ResendAvailableAt = DateTime.UtcNow
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ConfirmEmailRequestInput!) {
                                  confirmEmail(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code = "9999"
            }
        });

        var errors = doc.GetErrors();

        errors.Should().NotBeNull();

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("EMAIL_CONFIRMATION_CODE_ATTEMPTS_EXCEEDED");

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("attemptsLeft")
            .GetInt32()
            .Should()
            .Be(0);

        Context.ChangeTracker.Clear();

        var verification = await Context.UserEmailVerifications.AsNoTracking().SingleAsync();

        verification.Attempts.Should().Be(RegistrationVerification.MaxAttempts);
    }
    
    [Fact]
    public async Task ConfirmEmail_WhenEmailChanged_ReturnsEmailChangedError()
    {
        await ResetAsync();

        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();

        const string code = "1234";

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "current@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = false
        });

        Context.UserEmailVerifications.Add(new UserEmailVerification
        {
            UserId = UserId,
            Type = EmailVerificationType.EmailConfirmation,
            Email = "old@example.com",
            CodeHash = cryptoService.HashCode(code),
            Attempts = 0,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            ResendAvailableAt = DateTime.UtcNow
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ConfirmEmailRequestInput!) {
                                  confirmEmail(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code
            }
        });

        var errors = doc.GetErrors();

        errors.Should().NotBeNull();

        errors.Value[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("EMAIL_CONFIRMATION_EMAIL_CHANGED");

        Context.ChangeTracker.Clear();

        var credential = await Context.UserCredentials.AsNoTracking().SingleAsync(x => x.UserId == UserId);

        credential.IsEmailConfirmed.Should().BeFalse();
        (await Context.UserEmailVerifications.AsNoTracking().CountAsync()).Should().Be(1);
    }
    
    [Fact]
    public async Task ConfirmEmail_Success_ConfirmsEmailAndRemovesVerification()
    {
        await ResetAsync();

        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();

        const string code = "1234";
        const string email = "success@example.com";

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = false
        });

        Context.UserEmailVerifications.Add(new UserEmailVerification
        {
            UserId = UserId,
            Type = EmailVerificationType.EmailConfirmation,
            Email = email,
            CodeHash = cryptoService.HashCode(code),
            Attempts = 0,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            ResendAvailableAt = DateTime.UtcNow
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ConfirmEmailRequestInput!) {
                                  confirmEmail(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code
            }
        });

        doc.GetErrors().Should().BeNull();

        doc.GetData()
            .GetProperty("confirmEmail")
            .GetBoolean()
            .Should()
            .BeTrue();

        Context.ChangeTracker.Clear();

        var credential = await Context.UserCredentials.AsNoTracking().SingleAsync(x => x.UserId == UserId);

        credential.IsEmailConfirmed.Should().BeTrue();
        (await Context.UserEmailVerifications.AsNoTracking().CountAsync()).Should().Be(0);
    }
    
    [Fact]
    public async Task ChangePassword_WhenNewPasswordHasNoLowercase_ReturnsValidationError()
    {
        await ResetAsync();

        const string currentPassword = "Password1!";

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "password-validation-lower@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(currentPassword),
            IsEmailConfirmed = true
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ChangePasswordRequestInput!) {
                                  changePassword(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                currentPassword,
                newPassword = "AAAAAAAA1!",
                confirmPassword = "AAAAAAAA1!"
            }
        });

        doc.GetErrors().Should().NotBeNull();
    }
    
    [Fact]
    public async Task ChangePassword_WhenNewPasswordHasNoUppercaseDigitOrSpecial_ReturnsValidationError()
    {
        await ResetAsync();

        const string currentPassword = "Password1!";

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = UserId,
            Email = "password-validation-other@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(currentPassword),
            IsEmailConfirmed = true
        });

        await Context.SaveChangesAsync();

        const string mutation = """
                                mutation ($request: ChangePasswordRequestInput!) {
                                  changePassword(request: $request)
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                currentPassword,
                newPassword = "aaaaaaaa",
                confirmPassword = "aaaaaaaa"
            }
        });

        doc.GetErrors().Should().NotBeNull();
    }
    
    [Fact]
    public async Task Login_WhenHttpContextIsNull_SavesNullClientMetadata()
    {
        await ResetAsync();

        const string password = "Password1!";

        var userId = Guid.NewGuid();

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = userId,
            Email = "null-context@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsEmailConfirmed = true
        });

        await Context.SaveChangesAsync();

        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();

        var mutation = new Mutation(tokenService, new HttpContextAccessor { HttpContext = null });

        var response = await mutation.Login(
            new LoginRequest
            {
                Email = "null-context@example.com",
                Password = password
            }, Context, CancellationToken.None);

        response.AccessToken.Should().NotBeNullOrWhiteSpace();
        response.RefreshToken.Should().NotBeNullOrWhiteSpace();

        Context.ChangeTracker.Clear();

        var refreshToken = await Context.RefreshTokens.AsNoTracking().SingleAsync(x => x.UserId == userId);

        refreshToken.CreatedByIp.Should().BeNull();
        refreshToken.UserAgent.Should().BeNull();
    }
    
    [Fact]
    public async Task Login_WhenRemoteIpIsNull_SavesNullClientIp()
    {
        await ResetAsync();

        const string password = "Password1!";

        var userId = Guid.NewGuid();

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = userId,
            Email = "null-ip@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsEmailConfirmed = true
        });

        await Context.SaveChangesAsync();

        var httpContext = new DefaultHttpContext();

        httpContext.Connection.RemoteIpAddress = null;
        httpContext.Request.Headers.UserAgent = "Planara.Tests/NullIp";

        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();

        var mutation = new Mutation(tokenService, new HttpContextAccessor { HttpContext = httpContext });

        await mutation.Login(
            new LoginRequest
            {
                Email = "null-ip@example.com",
                Password = password
            }, Context, CancellationToken.None);

        Context.ChangeTracker.Clear();

        var refreshToken = await Context.RefreshTokens.AsNoTracking().SingleAsync(x => x.UserId == userId);

        refreshToken.CreatedByIp.Should().BeNull();
        refreshToken.UserAgent.Should().Be("Planara.Tests/NullIp");
    }
    
    [Fact]
    public async Task Login_WhenClientMetadataExists_SavesIpAndUserAgent()
    {
        await ResetAsync();

        const string password = "Password1!";

        var userId = Guid.NewGuid();

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = userId,
            Email = "metadata@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsEmailConfirmed = true
        });

        await Context.SaveChangesAsync();

        var httpContext = new DefaultHttpContext();

        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        httpContext.Request.Headers.UserAgent = "Planara.Tests/Metadata";

        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();

        var mutation = new Mutation(tokenService, new HttpContextAccessor { HttpContext = httpContext });

        await mutation.Login(
            new LoginRequest
            {
                Email = "metadata@example.com",
                Password = password
            }, Context, CancellationToken.None);

        Context.ChangeTracker.Clear();

        var refreshToken = await Context.RefreshTokens.AsNoTracking().SingleAsync(x => x.UserId == userId);

        refreshToken.CreatedByIp.Should().Be("127.0.0.1");
        refreshToken.UserAgent.Should().Be("Planara.Tests/Metadata");
    }
}