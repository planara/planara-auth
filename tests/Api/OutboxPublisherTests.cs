using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planara.Auth.Data.Domain;
using Planara.Auth.Workers;
using Planara.Common.Kafka;
using Planara.Kafka.Configurations;

namespace Planara.Auth.Tests.Api;

[Collection("AuthApi")]
public class OutboxPublisherTests : BaseApiTest
{
    public OutboxPublisherTests(ApiTestWebAppFactory factory) : base(factory) { }

    [Fact]
    public async Task PublishOnce_SendsMessage_AndMarksProcessed()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var userId = Guid.NewGuid();
        Context.OutboxMessages.Add(new OutboxMessage
        {
            TopicKey = "Auth",
            Type = nameof(UserCreatedMessage),
            Key = userId.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(
                new UserCreatedMessage { UserId = userId, Email = "a@b.com" },
                KafkaJson.SerializerOptions),
        });
        await Context.SaveChangesAsync();

        using var scope = Factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<OutboxPublisher>();
        var fake = scope.ServiceProvider.GetRequiredService<FakeKafkaProducer>();
        fake.ThrowOnProduce = false;

        await publisher.PublishOnce(CancellationToken.None);

        fake.Sent.Should().HaveCount(1);
        fake.Sent[0].TopicKey.Should().Be("Auth");
        fake.Sent[0].Msg.Email.Should().Be("a@b.com");
        
        Context.ChangeTracker.Clear();

        var row = await Context.OutboxMessages
            .AsNoTracking()
            .SingleAsync();

        row.ProcessedAt.Should().NotBeNull();
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
            PayloadJson = JsonSerializer.Serialize(
                new UserCreatedMessage { UserId = userId, Email = "a@b.com" },
                KafkaJson.SerializerOptions),
        });
        await Context.SaveChangesAsync();

        using var scope = Factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<OutboxPublisher>();
        var fake = scope.ServiceProvider.GetRequiredService<FakeKafkaProducer>();

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
    public async Task PublishOnce_WhenNoMessages_DoesNotProduce()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        using var scope = Factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<OutboxPublisher>();
        var fake = scope.ServiceProvider.GetRequiredService<FakeKafkaProducer>();

        var before = DateTime.UtcNow;
        await publisher.PublishOnce(CancellationToken.None);
        var after = DateTime.UtcNow;

        fake.Sent.Should().BeEmpty();
        
        (after - before).Should().BeGreaterThan(TimeSpan.FromMilliseconds(200));
    }
}