using Planara.Kafka.Interfaces;

namespace Planara.Auth.Tests;

public class FakeKafkaProducer<TMessage> : IKafkaProducer<TMessage>
{
    public List<ProducedMessage<TMessage>> Sent { get; } = [];

    public bool ThrowOnProduce { get; set; }

    public Exception ExceptionToThrow { get; set; } = new InvalidOperationException("Produce failed");

    public Task ProduceAsync(
        string topicKey,
        string key,
        TMessage message,
        CancellationToken cancellationToken = default)
    {
        if (ThrowOnProduce)
            throw ExceptionToThrow;

        Sent.Add(new ProducedMessage<TMessage>(topicKey, key, message));

        return Task.CompletedTask;
    }

    public void Reset()
    {
        Sent.Clear();
        ThrowOnProduce = false;
        ExceptionToThrow = new InvalidOperationException("Produce failed");
    }

    public sealed record ProducedMessage<T>(
        string TopicKey,
        string Key,
        T Msg);
}