using Planara.Common.Kafka;
using Planara.Kafka.Interfaces;

namespace Planara.Auth.Tests;

public sealed class FakeKafkaProducer: IKafkaProducer<UserCreatedMessage>
{
    public List<(string TopicKey, string Key, UserCreatedMessage Msg)> Sent { get; } = new();

    public Task ProduceAsync(string topicKey, string key, UserCreatedMessage message, CancellationToken cancellationToken = default)
    {
        Sent.Add((topicKey, key, message));
        return Task.CompletedTask;
    }
}