using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Planara.Auth.Data;
using Planara.Auth.Data.Domain;
using Planara.Common.Kafka;
using Planara.Kafka.Configurations;

namespace Planara.Auth.Services;

public static class RegistrationVerification
{
    public static async Task IssueCodeAsync(
        RegistrationSession registration,
        DataContext dataContext,
        IRegistrationCryptoService cryptoService,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var verification = await dataContext
            .RegistrationEmailVerifications
            .SingleOrDefaultAsync(x => x.RegistrationSessionId == registration.Id, cancellationToken);
        
        if (verification is not null && verification.ExpiresAt > now && verification.ResendAvailableAt > now)
            return;

        var code = cryptoService.GenerateCode();

        if (verification is null)
        {
            verification = new RegistrationEmailVerification
            {
                RegistrationSessionId = registration.Id,
                CodeHash = cryptoService.HashCode(code),
                Attempts = 0,
                ExpiresAt = now.AddMinutes(10),
                ResendAvailableAt = now.AddSeconds(10)
            };

            await dataContext
                .RegistrationEmailVerifications.AddAsync(verification, cancellationToken);
        }
        else
        {
            verification.CodeHash = cryptoService.HashCode(code);
            verification.Attempts = 0;
            verification.ExpiresAt = now.AddMinutes(10);
            verification.ResendAvailableAt = now.AddSeconds(10);
        }

        var message = new EmailConfirmationMessage { Email = registration.Email!, Code = code };

        await dataContext.OutboxMessages.AddAsync(
            new OutboxMessage
            {
                TopicKey = KafkaTopicKeys.EmailConfirmation,
                Type = nameof(EmailConfirmationMessage),
                Key = registration.Email!,
                PayloadJson = JsonSerializer.Serialize(message, KafkaJson.SerializerOptions)
            },
            cancellationToken);
    }
}