using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planara.Auth.Data.Domain;
using Planara.Auth.Data.Enums;
using Planara.Auth.Workers;
using Planara.Common.Database.Domain;

namespace Planara.Auth.Tests.Api;

public class CleanupWorkerTests : BaseApiTest
{
    public CleanupWorkerTests(ApiTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task RegistrationCleanupWorker_RemovesExpiredSessions()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var now = DateTime.UtcNow;

        var expiredId = Guid.NewGuid();
        var activeId = Guid.NewGuid();

        Context.RegistrationSessions.AddRange(
            new RegistrationSession
            {
                Id = expiredId,
                Email = "expired@planara.test",
                SessionTokenHash = "expired-session-hash",
                CurrentStep = RegistrationStep.Email,
                NextStep = RegistrationStep.Code,
                ExpiresAt = now.AddMinutes(-1)
            },
            new RegistrationSession
            {
                Id = activeId,
                Email = "active@planara.test",
                SessionTokenHash = "active-session-hash",
                CurrentStep = RegistrationStep.Email,
                NextStep = RegistrationStep.Code,
                ExpiresAt = now.AddHours(1)
            });

        await Context.SaveChangesAsync();

        using var scope = Factory.Services.CreateScope();

        var worker = ActivatorUtilities.CreateInstance<RegistrationCleanupWorker>(scope.ServiceProvider);

        GetProtectedProperty<string>(worker, "WorkerName").Should().Be(nameof(RegistrationCleanupWorker));
        GetProtectedProperty<TimeSpan>(worker, "CheckInterval").Should().Be(TimeSpan.FromMinutes(5));

        var deleted = await InvokeCleanupAsync(worker, Context, now, 100);

        deleted.Should().Be(1);

        Context.ChangeTracker.Clear();

        (await Context.RegistrationSessions.AsNoTracking().AnyAsync(x => x.Id == expiredId)).Should().BeFalse();
        (await Context.RegistrationSessions.AsNoTracking().AnyAsync(x => x.Id == activeId)).Should().BeTrue();

        var deletedWhenNothingExpired = await InvokeCleanupAsync(worker, Context, now, 100);

        deletedWhenNothingExpired.Should().Be(0);
    }

    [Fact]
    public async Task RefreshTokenCleanupWorker_RemovesExpiredTokens()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();

        Context.UserCredentials.Add(new UserCredential
        {
            UserId = userId,
            Email = "cleanup-refresh@planara.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password1!"),
            IsEmailConfirmed = true
        });

        var expiredId = Guid.NewGuid();
        var activeId = Guid.NewGuid();

        Context.RefreshTokens.AddRange(
            new RefreshToken
            {
                Id = expiredId,
                UserId = userId,
                TokenHash = "expired-refresh-token",
                CreatedAtUtc = now.AddDays(-30),
                ExpiresAtUtc = now.AddMinutes(-1)
            },
            new RefreshToken
            {
                Id = activeId,
                UserId = userId,
                TokenHash = "active-refresh-token",
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(30)
            });

        await Context.SaveChangesAsync();

        using var scope = Factory.Services.CreateScope();

        var worker = ActivatorUtilities.CreateInstance<RefreshTokenCleanupWorker>(scope.ServiceProvider);

        GetProtectedProperty<string>(worker, "WorkerName").Should().Be(nameof(RefreshTokenCleanupWorker));
        GetProtectedProperty<int>(worker, "BatchSize").Should().Be(500);
        GetProtectedProperty<TimeSpan>(worker, "CheckInterval").Should().Be(TimeSpan.FromMinutes(30));

        var deleted = await InvokeCleanupAsync(worker, Context, now, 500);

        deleted.Should().Be(1);

        Context.ChangeTracker.Clear();

        (await Context.RefreshTokens.AsNoTracking().AnyAsync(x => x.Id == expiredId)).Should().BeFalse();
        (await Context.RefreshTokens.AsNoTracking().AnyAsync(x => x.Id == activeId)).Should().BeTrue();

        var deletedWhenNothingExpired = await InvokeCleanupAsync(worker, Context, now, 500);

        deletedWhenNothingExpired.Should().Be(0);
    }

    [Fact]
    public async Task OutboxCleanupWorker_RemovesProcessedMessagesOlderThanRetentionPeriod()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var now = DateTime.UtcNow;

        var oldId = Guid.NewGuid();
        var recentId = Guid.NewGuid();
        var unprocessedId = Guid.NewGuid();

        Context.OutboxMessages.AddRange(
            new OutboxMessage
            {
                Id = oldId,
                TopicKey = "cleanup",
                Type = "Old",
                Key = oldId.ToString("N"),
                PayloadJson = "{}",
                ProcessedAt = now.AddDays(-8)
            },
            new OutboxMessage
            {
                Id = recentId,
                TopicKey = "cleanup",
                Type = "Recent",
                Key = recentId.ToString("N"),
                PayloadJson = "{}",
                ProcessedAt = now.AddDays(-1)
            },
            new OutboxMessage
            {
                Id = unprocessedId,
                TopicKey = "cleanup",
                Type = "Unprocessed",
                Key = unprocessedId.ToString("N"),
                PayloadJson = "{}",
                ProcessedAt = null
            });

        await Context.SaveChangesAsync();

        using var scope = Factory.Services.CreateScope();

        var worker = ActivatorUtilities.CreateInstance<OutboxCleanupWorker>(scope.ServiceProvider);

        GetProtectedProperty<string>(worker, "WorkerName").Should().Be(nameof(OutboxCleanupWorker));
        GetProtectedProperty<int>(worker, "BatchSize").Should().Be(500);
        GetProtectedProperty<TimeSpan>(worker, "CheckInterval").Should().Be(TimeSpan.FromMinutes(30));

        var deleted = await InvokeCleanupAsync(worker, Context, now, 500);

        deleted.Should().Be(1);

        Context.ChangeTracker.Clear();

        (await Context.OutboxMessages.AsNoTracking().AnyAsync(x => x.Id == oldId)).Should().BeFalse();
        (await Context.OutboxMessages.AsNoTracking().AnyAsync(x => x.Id == recentId)).Should().BeTrue();
        (await Context.OutboxMessages.AsNoTracking().AnyAsync(x => x.Id == unprocessedId)).Should().BeTrue();

        var deletedWhenNothingIsOldEnough = await InvokeCleanupAsync(worker, Context, now, 500);

        deletedWhenNothingIsOldEnough.Should().Be(0);
    }

    private static async Task<int> InvokeCleanupAsync(object worker, DbContext dataContext, DateTime now, int batchSize)
    {
        var method = worker
            .GetType()
            .GetMethod("CleanupAsync", BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();

        var result = method!.Invoke(
            worker,
            [
                dataContext,
                now,
                batchSize,
                CancellationToken.None
            ]);

        result.Should().BeAssignableTo<Task<int>>();

        return await (Task<int>)result!;
    }

    private static T GetProtectedProperty<T>(object worker, string propertyName)
    {
        var property = worker.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic);

        property.Should().NotBeNull();

        return (T)property!.GetValue(worker)!;
    }
}