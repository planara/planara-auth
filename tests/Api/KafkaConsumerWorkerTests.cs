using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planara.Auth.Workers;
using Planara.Common.Database.Domain;
using Planara.Common.Enums;
using Planara.Common.Kafka;
using Planara.Common.Kafka.Messages.Privacy;

namespace Planara.Auth.Tests.Api;

public class KafkaConsumerWorkerTests: BaseApiTest
{
    public KafkaConsumerWorkerTests(ApiTestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task HandleMessage_WithUserId_CreatesProjection()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        using var scope = Factory.Services.CreateScope();

        var worker = ActivatorUtilities.CreateInstance<ConsentGrantedKafkaConsumerWorker>(scope.ServiceProvider);

        GetTopicKey(worker).Should().Be(KafkaTopicKeys.ConsentGranted);

        var userId = Guid.NewGuid();
        var consentVersionId = Guid.NewGuid();
        var givenAt = DateTime.UtcNow.AddMinutes(-1);
        var before = DateTime.UtcNow;

        await InvokeHandleMessageAsync(worker,
            new ConsentGrantedMessage
            {
                UserId = userId,
                RegistrationId = null,
                Type = ConsentType.PersonalData,
                ConsentVersionId = consentVersionId,
                GivenAt = givenAt
            }, scope.ServiceProvider);

        Context.ChangeTracker.Clear();

        var projection = await Context.UserConsentProjections.AsNoTracking().SingleAsync();

        projection.UserId.Should().Be(userId);
        projection.Type.Should().Be(ConsentType.PersonalData);
        projection.ConsentVersionId.Should().Be(consentVersionId);
        projection.IsGranted.Should().BeTrue();
        projection.GrantedAt.Should().BeCloseTo(givenAt, TimeSpan.FromMicroseconds(1));
        projection.RevokedAt.Should().BeNull();
        projection.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task HandleMessage_WithRegistrationId_UpdatesExistingProjection()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var registrationId = Guid.NewGuid();
        var oldConsentVersionId = Guid.NewGuid();
        var newConsentVersionId = Guid.NewGuid();

        Context.UserConsentProjections.Add(new UserConsentProjection
        {
            UserId = registrationId,
            Type = ConsentType.PersonalData,
            ConsentVersionId = oldConsentVersionId,
            IsGranted = false,
            GrantedAt = DateTime.UtcNow.AddDays(-10),
            RevokedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow.AddDays(-1)
        });

        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        using var scope = Factory.Services.CreateScope();

        var worker = ActivatorUtilities.CreateInstance<ConsentGrantedKafkaConsumerWorker>(scope.ServiceProvider);

        var givenAt = DateTime.UtcNow.AddMinutes(-1);
        var before = DateTime.UtcNow;

        await InvokeHandleMessageAsync(worker,
            new ConsentGrantedMessage
            {
                UserId = null,
                RegistrationId = registrationId,
                Type = ConsentType.PersonalData,
                ConsentVersionId = newConsentVersionId,
                GivenAt = givenAt
            }, scope.ServiceProvider);

        Context.ChangeTracker.Clear();

        var projection = await Context.UserConsentProjections.AsNoTracking().SingleAsync();

        projection.UserId.Should().Be(registrationId);
        projection.Type.Should().Be(ConsentType.PersonalData);
        projection.ConsentVersionId.Should().Be(newConsentVersionId);
        projection.IsGranted.Should().BeTrue();
        projection.GrantedAt.Should().Be(givenAt);
        projection.RevokedAt.Should().BeNull();
        projection.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task HandleMessage_WhenSubjectIsMissing_Throws()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        using var scope = Factory.Services.CreateScope();

        var worker = ActivatorUtilities.CreateInstance<ConsentGrantedKafkaConsumerWorker>(scope.ServiceProvider);

        var message = new ConsentGrantedMessage
        {
            UserId = null,
            RegistrationId = null,
            Type = ConsentType.PersonalData,
            ConsentVersionId = Guid.NewGuid(),
            GivenAt = DateTime.UtcNow
        };

        Func<Task> act = () => InvokeHandleMessageAsync(worker, message, scope.ServiceProvider);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Exactly one consent subject must be specified: RegistrationId or UserId");
    }

    [Fact]
    public async Task HandleMessage_WhenBothSubjectsAreSpecified_Throws()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        using var scope = Factory.Services.CreateScope();

        var worker = ActivatorUtilities.CreateInstance<ConsentGrantedKafkaConsumerWorker>(scope.ServiceProvider);

        var message = new ConsentGrantedMessage
        {
            UserId = Guid.NewGuid(),
            RegistrationId = Guid.NewGuid(),
            Type = ConsentType.PersonalData,
            ConsentVersionId = Guid.NewGuid(),
            GivenAt = DateTime.UtcNow
        };

        Func<Task> act = () => InvokeHandleMessageAsync(worker, message, scope.ServiceProvider);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Exactly one consent subject must be specified: RegistrationId or UserId");
    }

    private static async Task InvokeHandleMessageAsync(ConsentGrantedKafkaConsumerWorker worker, ConsentGrantedMessage message, IServiceProvider serviceProvider)
    {
        var method = typeof(ConsentGrantedKafkaConsumerWorker).GetMethod("HandleMessage", BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();

        var result = method!.Invoke(
            worker,
            [
                message,
                serviceProvider,
                CancellationToken.None
            ]);

        result.Should().BeAssignableTo<Task>();

        await (Task)result!;
    }

    private static string GetTopicKey(ConsentGrantedKafkaConsumerWorker worker)
    {
        var property = typeof(ConsentGrantedKafkaConsumerWorker).GetProperty("TopicKey", BindingFlags.Instance | BindingFlags.NonPublic);

        property.Should().NotBeNull();

        return (string)property!.GetValue(worker)!;
    }
}