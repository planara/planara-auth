using Planara.Common.Kafka;
using Planara.Kafka.Interfaces;

namespace Planara.Auth.Workers;

public class EmailConfirmationOutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IKafkaProducer<EmailConfirmationMessage> producer,
    ILogger<UserCreatedOutboxPublisher> logger
) : OutboxPublisherBase<EmailConfirmationMessage>(scopeFactory, producer, logger);