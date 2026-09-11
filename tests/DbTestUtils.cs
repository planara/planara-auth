using Microsoft.EntityFrameworkCore;
using Planara.Auth.Data;

namespace Planara.Auth.Tests;

public class DbTestUtils
{
    public static async Task ResetAuthDbAsync(DataContext db, CancellationToken cancellationToken = default)
    {
        await db.Database
            .ExecuteSqlRawAsync(@"TRUNCATE TABLE ""RegistrationEmailVerifications"" RESTART IDENTITY CASCADE;", cancellationToken);
        await db.Database
            .ExecuteSqlRawAsync(@"TRUNCATE TABLE ""UserEmailVerifications"" RESTART IDENTITY CASCADE;", cancellationToken);
        await db.Database
            .ExecuteSqlRawAsync(@"TRUNCATE TABLE ""RefreshTokens"" RESTART IDENTITY CASCADE;", cancellationToken);
        await db.Database
            .ExecuteSqlRawAsync(@"TRUNCATE TABLE ""UserConsentProjections"" RESTART IDENTITY CASCADE;", cancellationToken);
        await db.Database
            .ExecuteSqlRawAsync(@"TRUNCATE TABLE ""RegistrationSessions"" RESTART IDENTITY CASCADE;", cancellationToken);
        await db.Database
            .ExecuteSqlRawAsync(@"TRUNCATE TABLE ""OutboxMessages"" RESTART IDENTITY CASCADE;", cancellationToken);
        await db.Database
            .ExecuteSqlRawAsync(@"TRUNCATE TABLE ""UserCredentials"" RESTART IDENTITY CASCADE;", cancellationToken);
        db.ChangeTracker.Clear();
    }
}