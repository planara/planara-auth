using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Planara.Auth.Data.Domain;
using Planara.Auth.Workers;
using Planara.Common.Kafka;
using Planara.Kafka.Configurations;
using Planara.Kafka.Interfaces;

namespace Planara.Auth.Tests.Api;

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
        var publisher = scope.ServiceProvider
            .GetServices<IHostedService>()
            .OfType<OutboxPublisher>()
            .Single();

        var fake = (FakeKafkaProducer)scope.ServiceProvider
            .GetRequiredService<IKafkaProducer<UserCreatedMessage>>();

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
}