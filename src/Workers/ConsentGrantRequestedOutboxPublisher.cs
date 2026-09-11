using Planara.Common.Kafka.Messages.Privacy;
using Planara.Common.Workers;
using Planara.Kafka.Interfaces;

namespace Planara.Auth.Workers;

public class ConsentGrantRequestedOutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IKafkaProducer<ConsentGrantRequestedMessage> producer,
    ILogger<UserCreatedOutboxPublisher> logger
) : OutboxPublisherBase<ConsentGrantRequestedMessage>(scopeFactory, producer, logger);