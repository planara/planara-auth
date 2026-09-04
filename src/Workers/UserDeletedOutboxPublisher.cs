using Planara.Common.Kafka.Messages.Auth;
using Planara.Common.Workers;
using Planara.Kafka.Interfaces;

namespace Planara.Auth.Workers;

public class UserDeletedOutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IKafkaProducer<UserDeletedMessage> producer,
    ILogger<UserDeletedOutboxPublisher> logger
) : OutboxPublisherBase<UserDeletedMessage>(scopeFactory, producer, logger);