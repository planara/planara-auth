using Microsoft.EntityFrameworkCore;
using Planara.Auth.Data.Domain;
using Planara.Common.Database;
using Planara.Common.Database.Domain;

namespace Planara.Auth.Data;

public class DataContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<UserCredential> UserCredentials { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;
    public DbSet<RegistrationSession> RegistrationSessions { get; set; } = null!;
    public DbSet<RegistrationEmailVerification> RegistrationEmailVerifications { get; set; } = null!;
    public DbSet<UserConsentProjection> UserConsentProjections { get; set; } = null!;
    public DbSet<UserEmailVerification> UserEmailVerifications { get; set; } = null!;
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder
            .AddOutbox()
            .AddConsentProjections();

        modelBuilder.Entity<UserCredential>()
            .HasKey(x => x.UserId);

        modelBuilder.Entity<UserCredential>()
            .HasIndex(x => x.Email)
            .IsUnique();
        
        modelBuilder.Entity<RegistrationSession>()
            .HasIndex(x => x.Email)
            .IsUnique();
        
        modelBuilder.Entity<RegistrationSession>()
            .HasIndex(x => x.SessionTokenHash)
            .IsUnique();
        
        modelBuilder.Entity<RegistrationEmailVerification>()
            .HasIndex(x => x.RegistrationSessionId)
            .IsUnique();
        
        modelBuilder.Entity<RegistrationSession>()
            .HasOne(x => x.EmailVerification)
            .WithOne(x => x.RegistrationSession)
            .HasForeignKey<RegistrationEmailVerification>(x => x.RegistrationSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(x => x.TokenHash)
            .IsUnique();

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(x => x.UserId);
        
        modelBuilder.Entity<UserEmailVerification>()
            .HasOne(x => x.UserCredential)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserEmailVerification>()
            .HasIndex(x => new { x.UserId, x.Type })
            .IsUnique();
    }
}