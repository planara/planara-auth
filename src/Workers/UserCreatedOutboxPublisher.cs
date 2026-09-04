using Planara.Common.Kafka.Messages.Auth;
using Planara.Common.Workers;
using Planara.Kafka.Interfaces;

namespace Planara.Auth.Workers;

public class UserCreatedOutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IKafkaProducer<UserCreatedMessage> producer,
    ILogger<UserCreatedOutboxPublisher> logger
) : OutboxPublisherBase<UserCreatedMessage>(scopeFactory, producer, logger);