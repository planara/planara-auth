using Microsoft.EntityFrameworkCore;
using Planara.Auth.Data;
using Planara.Common.Database.Domain;
using Planara.Common.Kafka;
using Planara.Common.Kafka.Messages.Privacy;
using Planara.Common.Workers;
using Planara.Kafka.Interfaces;

namespace Planara.Auth.Workers;

public class ConsentGrantedKafkaConsumerWorker(
    ILogger<ConsentGrantedKafkaConsumerWorker> logger,
    IKafkaConsumer<ConsentGrantedMessage> consumer,
    IServiceScopeFactory scopeFactory)
    : KafkaConsumerWorkerBase<ConsentGrantedMessage>(logger, consumer, scopeFactory)
{
    protected override string TopicKey => KafkaTopicKeys.ConsentGranted;
    
    protected override async Task HandleMessage(
        ConsentGrantedMessage message,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var dataContext = serviceProvider.GetRequiredService<DataContext>();

        var userId = GetSubjectId(message);
        var projection = await dataContext.UserConsentProjections
            .SingleOrDefaultAsync(x => x.UserId == userId && x.Type == message.Type, cancellationToken);
        var now = DateTime.UtcNow;

        if (projection is null)
        {
            projection = new UserConsentProjection
            {
                UserId = userId,
                Type = message.Type,
                ConsentVersionId = message.ConsentVersionId,
                IsGranted = true,
                GrantedAt = message.GivenAt,
                RevokedAt = null,
                UpdatedAt = now
            };

            dataContext.UserConsentProjections.Add(projection);
        }
        else
        {
            projection.ConsentVersionId = message.ConsentVersionId;
            projection.IsGranted = true;
            projection.GrantedAt = message.GivenAt;
            projection.RevokedAt = null;
            projection.UpdatedAt = now;
        }

        await dataContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Consent projection updated. UserId: {UserId}, Type: {ConsentType}, ConsentVersionId: {ConsentVersionId}.", userId, message.Type, message.ConsentVersionId);
    }
    
    private static Guid GetSubjectId(ConsentGrantedMessage message)
    {
        if (message.UserId.HasValue == message.RegistrationId.HasValue)
            throw new InvalidOperationException("Exactly one consent subject must be specified: RegistrationId or UserId");

        return message.UserId ?? message.RegistrationId!.Value;
    }
}