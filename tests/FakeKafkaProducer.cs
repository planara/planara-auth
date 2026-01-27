using Planara.Common.Kafka;
using Planara.Kafka.Interfaces;

namespace Planara.Auth.Tests;

public sealed class FakeKafkaProducer: IKafkaProducer<UserCreatedMessage>
{
    public List<(string TopicKey, string Key, UserCreatedMessage Msg)> Sent { get; } = new();

    public bool ThrowOnProduce { get; set; }
    public Exception? ExceptionToThrow { get; set; }

    public Task ProduceAsync(string topicKey, string key, UserCreatedMessage message, CancellationToken cancellationToken = default)
    {
        if (ThrowOnProduce)
            throw ExceptionToThrow ?? new InvalidOperationException("boom");

        Sent.Add((topicKey, key, message));
        return Task.CompletedTask;
    }
}