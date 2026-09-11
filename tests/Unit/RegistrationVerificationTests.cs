using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planara.Auth.Data;
using Planara.Auth.Data.Domain;
using Planara.Auth.Services;
using Planara.Common.Kafka;
using Planara.Common.Kafka.Messages.Notifications;

namespace Planara.Auth.Tests.Unit;

public class RegistrationVerificationTests : IClassFixture<ApiTestWebAppFactory>, IDisposable
{
    private readonly IServiceScope _scope;
    private readonly DataContext _context;
    private readonly IRegistrationCryptoService _cryptoService;

    public RegistrationVerificationTests(ApiTestWebAppFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<DataContext>();
        _cryptoService = _scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
    }

    [Fact]
    public async Task IssueCodeAsync_VerificationDoesNotExist_CreatesVerification()
    {
        var registration = CreateRegistration();

        await _context.RegistrationSessions.AddAsync(registration);
        await _context.SaveChangesAsync();

        await RegistrationVerification.IssueCodeAsync(registration, _context, _cryptoService, CancellationToken.None);

        await _context.SaveChangesAsync();

        var verification = await _context.RegistrationEmailVerifications
            .SingleAsync(x => x.RegistrationSessionId == registration.Id);

        verification.Attempts.Should().Be(0);
        verification.CodeHash.Should().NotBeNullOrWhiteSpace();
        verification.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        verification.ResendAvailableAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task IssueCodeAsync_VerificationDoesNotExist_CreatesOutboxMessage()
    {
        var registration = CreateRegistration();

        await _context.RegistrationSessions.AddAsync(registration);
        await _context.SaveChangesAsync();

        await RegistrationVerification.IssueCodeAsync(registration, _context, _cryptoService, CancellationToken.None);

        await _context.SaveChangesAsync();

        var message = await _context.OutboxMessages
            .SingleAsync(x => x.Key == registration.Email);

        message.TopicKey.Should().Be(KafkaTopicKeys.EmailConfirmation);
        message.Type.Should().Be(nameof(EmailConfirmationMessage));
        message.Key.Should().Be(registration.Email);
        message.PayloadJson.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task IssueCodeAsync_VerificationExistsAndCooldownActive_DoesNothing()
    {
        var registration = CreateRegistration();

        var verification = new RegistrationEmailVerification
        {
            RegistrationSessionId = registration.Id,
            CodeHash = "old-hash",
            Attempts = 3,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            ResendAvailableAt = DateTime.UtcNow.AddMinutes(1)
        };

        await _context.RegistrationSessions.AddAsync(registration);
        await _context.RegistrationEmailVerifications.AddAsync(verification);
        await _context.SaveChangesAsync();

        await RegistrationVerification.IssueCodeAsync(registration, _context, _cryptoService, CancellationToken.None);

        await _context.SaveChangesAsync();

        var actualVerification = await _context.RegistrationEmailVerifications
            .SingleAsync(x => x.RegistrationSessionId == registration.Id);

        actualVerification.CodeHash.Should().Be("old-hash");
        actualVerification.Attempts.Should().Be(3);

        var outboxCount = await _context.OutboxMessages
            .CountAsync(x => x.Key == registration.Email);

        outboxCount.Should().Be(0);
    }

    [Fact]
    public async Task IssueCodeAsync_VerificationExistsAndCooldownExpired_UpdatesVerification()
    {
        var registration = CreateRegistration();

        var verification = new RegistrationEmailVerification
        {
            RegistrationSessionId = registration.Id,
            CodeHash = "old-hash",
            Attempts = 4,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            ResendAvailableAt = DateTime.UtcNow.AddSeconds(-1)
        };

        await _context.RegistrationSessions.AddAsync(registration);
        await _context.RegistrationEmailVerifications.AddAsync(verification);
        await _context.SaveChangesAsync();

        await RegistrationVerification.IssueCodeAsync(registration, _context, _cryptoService, CancellationToken.None);

        await _context.SaveChangesAsync();

        var actualVerification = await _context
            .RegistrationEmailVerifications
            .SingleAsync(x => x.RegistrationSessionId == registration.Id);

        actualVerification.CodeHash.Should().NotBe("old-hash");
        actualVerification.Attempts.Should().Be(0);
        actualVerification.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        actualVerification.ResendAvailableAt.Should().BeAfter(DateTime.UtcNow);

        var outboxCount = await _context.OutboxMessages
            .CountAsync(x => x.Key == registration.Email);

        outboxCount.Should().Be(1);
    }

    [Fact]
    public async Task IssueCodeAsync_VerificationExpired_UpdatesVerification()
    {
        var registration = CreateRegistration();

        var verification = new RegistrationEmailVerification
        {
            RegistrationSessionId = registration.Id,
            CodeHash = "old-hash",
            Attempts = 2,
            ExpiresAt = DateTime.UtcNow.AddSeconds(-1),
            ResendAvailableAt = DateTime.UtcNow.AddMinutes(1)
        };

        await _context.RegistrationSessions.AddAsync(registration);
        await _context.RegistrationEmailVerifications.AddAsync(verification);
        await _context.SaveChangesAsync();

        await RegistrationVerification.IssueCodeAsync(registration, _context, _cryptoService, CancellationToken.None);

        await _context.SaveChangesAsync();

        var actualVerification = await _context.RegistrationEmailVerifications
            .SingleAsync(x => x.RegistrationSessionId == registration.Id);

        actualVerification.CodeHash.Should().NotBe("old-hash");
        actualVerification.Attempts.Should().Be(0);

        var outboxCount = await _context.OutboxMessages
            .CountAsync(x => x.Key == registration.Email);

        outboxCount.Should().Be(1);
    }

    private static RegistrationSession CreateRegistration()
    {
        return new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@planara.ru",
            SessionTokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
    }

    public void Dispose() => _scope.Dispose();
}