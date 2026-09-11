using Confluent.Kafka;
using Planara.Kafka.Interfaces;

namespace Planara.Auth.Tests;

public class FakeKafkaConsumer<TMessage> : IKafkaConsumer<TMessage> where TMessage : class
{
    private readonly Queue<ConsumeResult<string, TMessage>> _messages = [];

    public List<ConsumeResult<string, TMessage>> Committed { get; } = [];

    public bool ThrowOnConsume { get; set; }
    public bool ThrowOnCommit { get; set; }
    public bool IsClosed { get; private set; }

    public Exception ConsumeExceptionToThrow { get; set; } = new InvalidOperationException("Consume failed");

    public Exception CommitExceptionToThrow { get; set; } = new InvalidOperationException("Commit failed");

    public Task<ConsumeResult<string, TMessage>?> ConsumeAsync(string topicKey, CancellationToken cancellationToken)
    {
        if (ThrowOnConsume)
            throw ConsumeExceptionToThrow;

        if (_messages.Count == 0)
            return Task.FromResult<ConsumeResult<string, TMessage>?>(null);

        return Task.FromResult<ConsumeResult<string, TMessage>?>(
            _messages.Dequeue());
    }

    public Task CommitAsync(ConsumeResult<string, TMessage> result, CancellationToken cancellationToken)
    {
        if (ThrowOnCommit)
            throw CommitExceptionToThrow;

        Committed.Add(result);

        return Task.CompletedTask;
    }

    public void Close() => IsClosed = true;

    public void Add(string topic, string key, TMessage message)
    {
        _messages.Enqueue(new ConsumeResult<string, TMessage>
        {
            Topic = topic,
            Partition = new Partition(0),
            Offset = new Offset(0),
            Message = new Message<string, TMessage>
            {
                Key = key,
                Value = message
            }
        });
    }

    public void Reset()
    {
        _messages.Clear();
        Committed.Clear();
        ThrowOnConsume = false;
        ThrowOnCommit = false;
        IsClosed = false;
        ConsumeExceptionToThrow = new InvalidOperationException("Consume failed");
        CommitExceptionToThrow = new InvalidOperationException("Commit failed");
    }
}