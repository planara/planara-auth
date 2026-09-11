using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planara.Auth.Workers;
using Planara.Common.Database.Domain;
using Planara.Common.Enums;
using Planara.Common.Kafka;
using Planara.Common.Kafka.Messages.Auth;
using Planara.Common.Kafka.Messages.Notifications;
using Planara.Common.Kafka.Messages.Privacy;
using Planara.Kafka.Configurations;

namespace Planara.Auth.Tests.Api;

public class OutboxPublisherTests : BaseApiTest
{
    public OutboxPublisherTests(ApiTestWebAppFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task UserCreatedPublisher_PublishOnce_SendsMessage_AndMarksProcessed()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var userId = Guid.NewGuid();

        Context.OutboxMessages.Add(new OutboxMessage
        {
            TopicKey = "Auth",
            Type = nameof(UserCreatedMessage),
            Key = userId.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(new UserCreatedMessage { UserId = userId, Email = "a@b.com" }, KafkaJson.SerializerOptions),
        });

        await Context.SaveChangesAsync();

        using var scope = Factory.Services.CreateScope();

        var publisher = scope.ServiceProvider.GetRequiredService<UserCreatedOutboxPublisher>();
        var fake = scope.ServiceProvider.GetRequiredService<FakeKafkaProducer<UserCreatedMessage>>();

        fake.Reset();

        await publisher.PublishOnce(CancellationToken.None);

        fake.Sent.Should().HaveCount(1);
        fake.Sent[0].TopicKey.Should().Be("Auth");
        fake.Sent[0].Key.Should().Be(userId.ToString("N"));
        fake.Sent[0].Msg.UserId.Should().Be(userId);
        fake.Sent[0].Msg.Email.Should().Be("a@b.com");

        Context.ChangeTracker.Clear();

        var row = await Context.OutboxMessages.AsNoTracking().SingleAsync();

        row.ProcessedAt.Should().NotBeNull();
        row.LastError.Should().BeNull();
        row.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task UserDeletedPublisher_PublishOnce_SendsMessage_AndMarksProcessed()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var userId = Guid.NewGuid();

        Context.OutboxMessages.Add(new OutboxMessage
        {
            TopicKey = "Auth",
            Type = nameof(UserDeletedMessage),
            Key = userId.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(new UserDeletedMessage { UserId = userId }, KafkaJson.SerializerOptions),
        });

        await Context.SaveChangesAsync();

        using var scope = Factory.Services.CreateScope();

        var publisher = scope.ServiceProvider.GetRequiredService<UserDeletedOutboxPublisher>();
        var fake = scope.ServiceProvider.GetRequiredService<FakeKafkaProducer<UserDeletedMessage>>();

        fake.Reset();

        await publisher.PublishOnce(CancellationToken.None);

        fake.Sent.Should().HaveCount(1);
        fake.Sent[0].TopicKey.Should().Be("Auth");
        fake.Sent[0].Key.Should().Be(userId.ToString("N"));
        fake.Sent[0].Msg.UserId.Should().Be(userId);

        Context.ChangeTracker.Clear();

        var row = await Context.OutboxMessages.AsNoTracking().SingleAsync();

        row.ProcessedAt.Should().NotBeNull();
        row.LastError.Should().BeNull();
        row.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task UserCreatedPublisher_PublishOnce_IgnoresUserDeletedMessages()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var userId = Guid.NewGuid();

        Context.OutboxMessages.Add(new OutboxMessage
        {
            TopicKey = "Auth",
            Type = nameof(UserDeletedMessage),
            Key = userId.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(new UserDeletedMessage { UserId = userId }, KafkaJson.SerializerOptions),
        });

        await Context.SaveChangesAsync();

        using var scope = Factory.Services.CreateScope();

        var publisher = scope.ServiceProvider.GetRequiredService<UserCreatedOutboxPublisher>();
        var fake = scope.ServiceProvider.GetRequiredService<FakeKafkaProducer<UserCreatedMessage>>();

        fake.Reset();

        await publisher.PublishOnce(CancellationToken.None);

        fake.Sent.Should().BeEmpty();

        Context.ChangeTracker.Clear();

        var row = await Context.OutboxMessages.AsNoTracking().SingleAsync();

        row.ProcessedAt.Should().BeNull();
        row.AttemptCount.Should().Be(0);
        row.LastError.Should().BeNull();
    }

    [Fact]
    public async Task UserDeletedPublisher_PublishOnce_IgnoresUserCreatedMessages()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var userId = Guid.NewGuid();

        Context.OutboxMessages.Add(new OutboxMessage
        {
            TopicKey = "Auth",
            Type = nameof(UserCreatedMessage),
            Key = userId.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(new UserCreatedMessage { UserId = userId, Email = "a@b.com" }, KafkaJson.SerializerOptions),
        });

        await Context.SaveChangesAsync();

        using var scope = Factory.Services.CreateScope();

        var publisher = scope.ServiceProvider.GetRequiredService<UserDeletedOutboxPublisher>();
        var fake = scope.ServiceProvider.GetRequiredService<FakeKafkaProducer<UserDeletedMessage>>();

        fake.Reset();

        await publisher.PublishOnce(CancellationToken.None);

        fake.Sent.Should().BeEmpty();

        Context.ChangeTracker.Clear();

        var row = await Context.OutboxMessages.AsNoTracking().SingleAsync();

        row.ProcessedAt.Should().BeNull();
        row.AttemptCount.Should().Be(0);
        row.LastError.Should().BeNull();
    }

    [Fact]
    public async Task PublishOnce_WhenProducerFails_IncrementsAttempt_AndSetsBackoff()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var userId = Guid.NewGuid();

        Context.OutboxMessages.Add(new OutboxMessage
        {
            TopicKey = "Auth",
            Type = nameof(UserCreatedMessage),
            Key = userId.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(new UserCreatedMessage { UserId = userId, Email = "a@b.com" }, KafkaJson.SerializerOptions),
        });

        await Context.SaveChangesAsync();

        using var scope = Factory.Services.CreateScope();

        var publisher = scope.ServiceProvider.GetRequiredService<UserCreatedOutboxPublisher>();
        var fake = scope.ServiceProvider.GetRequiredService<FakeKafkaProducer<UserCreatedMessage>>();

        fake.Reset();
        fake.ThrowOnProduce = true;
        fake.ExceptionToThrow = new InvalidOperationException("boom");

        var before = DateTime.UtcNow;

        await publisher.PublishOnce(CancellationToken.None);

        fake.Sent.Should().BeEmpty();

        Context.ChangeTracker.Clear();

        var row = await Context.OutboxMessages.AsNoTracking().SingleAsync();

        row.ProcessedAt.Should().BeNull();
        row.AttemptCount.Should().Be(1);
        row.LastAttemptAt.Should().NotBeNull();
        row.LastAttemptAt!.Value.Should().BeOnOrAfter(before);
        row.LastError.Should().NotBeNullOrWhiteSpace();
        row.LastError.Should().Contain("boom");
        row.LockedUntil.Should().NotBeNull();
        row.LockedUntil!.Value.Should().BeAfter(before);
        row.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task PublishOnce_WhenPayloadDeserializesToNull_IncrementsAttempt_AndSetsError()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var userId = Guid.NewGuid();

        Context.OutboxMessages.Add(new OutboxMessage
        {
            TopicKey = "Auth",
            Type = nameof(UserCreatedMessage),
            Key = userId.ToString("N"),
            PayloadJson = "null",
        });

        await Context.SaveChangesAsync();

        using var scope = Factory.Services.CreateScope();

        var publisher = scope.ServiceProvider.GetRequiredService<UserCreatedOutboxPublisher>();
        var fake = scope.ServiceProvider.GetRequiredService<FakeKafkaProducer<UserCreatedMessage>>();

        fake.Reset();

        await publisher.PublishOnce(CancellationToken.None);

        fake.Sent.Should().BeEmpty();

        Context.ChangeTracker.Clear();

        var row = await Context.OutboxMessages.AsNoTracking().SingleAsync();

        row.ProcessedAt.Should().BeNull();
        row.AttemptCount.Should().Be(1);
        row.LastError.Should().Contain("PayloadJson deserialized to null");
        row.LockedUntil.Should().NotBeNull();
    }

    [Fact]
    public async Task PublishOnce_WhenNoMessages_DoesNotProduce()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        using var scope = Factory.Services.CreateScope();

        var publisher = scope.ServiceProvider.GetRequiredService<UserCreatedOutboxPublisher>();
        var fake = scope.ServiceProvider.GetRequiredService<FakeKafkaProducer<UserCreatedMessage>>();

        fake.Reset();

        var before = DateTime.UtcNow;

        await publisher.PublishOnce(CancellationToken.None);

        var after = DateTime.UtcNow;

        fake.Sent.Should().BeEmpty();

        (after - before).Should().BeGreaterThan(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task PublishOnce_WhenMessageIsLocked_DoesNotProduce()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var userId = Guid.NewGuid();

        Context.OutboxMessages.Add(new OutboxMessage
        {
            TopicKey = "Auth",
            Type = nameof(UserCreatedMessage),
            Key = userId.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(new UserCreatedMessage { UserId = userId, Email = "a@b.com" }, KafkaJson.SerializerOptions),
            LockedUntil = DateTime.UtcNow.AddMinutes(5),
            LockedBy = "another-worker"
        });

        await Context.SaveChangesAsync();

        using var scope = Factory.Services.CreateScope();

        var publisher = scope.ServiceProvider.GetRequiredService<UserCreatedOutboxPublisher>();
        var fake = scope.ServiceProvider.GetRequiredService<FakeKafkaProducer<UserCreatedMessage>>();

        fake.Reset();

        await publisher.PublishOnce(CancellationToken.None);

        fake.Sent.Should().BeEmpty();

        Context.ChangeTracker.Clear();

        var row = await Context.OutboxMessages.AsNoTracking().SingleAsync();

        row.ProcessedAt.Should().BeNull();
        row.AttemptCount.Should().Be(0);
        row.LockedBy.Should().Be("another-worker");
    }
    
    [Fact]
    public async Task PublishOnce_WhenProducerFailsWithLongError_TruncatesLastError()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var userId = Guid.NewGuid();

        Context.OutboxMessages.Add(new OutboxMessage
        {
            TopicKey = "Auth",
            Type = nameof(UserCreatedMessage),
            Key = userId.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(new UserCreatedMessage { UserId = userId, Email = "a@b.com" }, KafkaJson.SerializerOptions),
        });

        await Context.SaveChangesAsync();

        using var scope = Factory.Services.CreateScope();

        var publisher = scope.ServiceProvider.GetRequiredService<UserCreatedOutboxPublisher>();
        var fake = scope.ServiceProvider.GetRequiredService<FakeKafkaProducer<UserCreatedMessage>>();

        fake.Reset();
        fake.ThrowOnProduce = true;
        fake.ExceptionToThrow = new InvalidOperationException(new string('x', 5000));

        await publisher.PublishOnce(CancellationToken.None);

        Context.ChangeTracker.Clear();

        var row = await Context.OutboxMessages.AsNoTracking().SingleAsync();

        row.ProcessedAt.Should().BeNull();
        row.AttemptCount.Should().Be(1);
        row.LastError.Should().NotBeNull();
        row.LastError!.Length.Should().Be(4000);
    }
    
    [Fact]
    public async Task ConsentGrantRequestedPublisher_PublishOnce_SendsMessage_AndMarksProcessed()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);
    
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var consentVersionId = Guid.NewGuid();
        var givenAt = DateTime.UtcNow;
    
        Context.OutboxMessages.Add(new OutboxMessage
        {
            TopicKey = KafkaTopicKeys.ConsentGrantRequested,
            Type = nameof(ConsentGrantRequestedMessage),
            Key = userId.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(
                new ConsentGrantRequestedMessage
                {
                    RequestId = requestId,
                    UserId = userId,
                    RegistrationId = null,
                    Type = ConsentType.PersonalData,
                    ConsentVersionId = consentVersionId,
                    GivenAt = givenAt,
                    IpAddress = "127.0.0.1",
                    UserAgent = "Planara.Tests/1.0"
                },
                KafkaJson.SerializerOptions)
        });
    
        await Context.SaveChangesAsync();
    
        using var scope = Factory.Services.CreateScope();
    
        var fake = scope.ServiceProvider.GetRequiredService<FakeKafkaProducer<ConsentGrantRequestedMessage>>();
    
        var publisher = ActivatorUtilities.CreateInstance<ConsentGrantRequestedOutboxPublisher>(scope.ServiceProvider);
    
        fake.Reset();
    
        await publisher.PublishOnce(CancellationToken.None);
    
        fake.Sent.Should().HaveCount(1);
        fake.Sent[0].TopicKey.Should().Be(KafkaTopicKeys.ConsentGrantRequested);
        fake.Sent[0].Key.Should().Be(userId.ToString("N"));
        fake.Sent[0].Msg.RequestId.Should().Be(requestId);
        fake.Sent[0].Msg.UserId.Should().Be(userId);
        fake.Sent[0].Msg.RegistrationId.Should().BeNull();
        fake.Sent[0].Msg.Type.Should().Be(ConsentType.PersonalData);
        fake.Sent[0].Msg.ConsentVersionId.Should().Be(consentVersionId);
        fake.Sent[0].Msg.GivenAt.Should().Be(givenAt);
    
        Context.ChangeTracker.Clear();
    
        var row = await Context.OutboxMessages.AsNoTracking().SingleAsync();
    
        row.ProcessedAt.Should().NotBeNull();
        row.LastError.Should().BeNull();
        row.LockedUntil.Should().BeNull();
    }
    
    [Fact]
    public async Task EmailConfirmationPublisher_PublishOnce_SendsMessage_AndMarksProcessed()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);
    
        var messageId = Guid.NewGuid();
    
        Context.OutboxMessages.Add(new OutboxMessage
        {
            TopicKey = KafkaTopicKeys.EmailConfirmation,
            Type = nameof(EmailConfirmationMessage),
            Key = messageId.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(
                new EmailConfirmationMessage
                {
                    Id = messageId,
                    Email = "confirmation@planara.test",
                    Code = "1234"
                },
                KafkaJson.SerializerOptions)
        });
    
        await Context.SaveChangesAsync();
    
        using var scope = Factory.Services.CreateScope();
    
        var fake = scope.ServiceProvider.GetRequiredService<FakeKafkaProducer<EmailConfirmationMessage>>();
    
        var publisher = ActivatorUtilities.CreateInstance<EmailConfirmationOutboxPublisher>(scope.ServiceProvider);
    
        fake.Reset();
    
        await publisher.PublishOnce(CancellationToken.None);
    
        fake.Sent.Should().HaveCount(1);
        fake.Sent[0].TopicKey.Should().Be(KafkaTopicKeys.EmailConfirmation);
        fake.Sent[0].Key.Should().Be(messageId.ToString("N"));
        fake.Sent[0].Msg.Id.Should().Be(messageId);
        fake.Sent[0].Msg.Email.Should().Be("confirmation@planara.test");
        fake.Sent[0].Msg.Code.Should().Be("1234");
    
        Context.ChangeTracker.Clear();
    
        var row = await Context.OutboxMessages.AsNoTracking().SingleAsync();
    
        row.ProcessedAt.Should().NotBeNull();
        row.LastError.Should().BeNull();
        row.LockedUntil.Should().BeNull();
    }
}