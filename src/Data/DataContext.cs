using Microsoft.EntityFrameworkCore;
using Planara.Auth.Data.Domain;

namespace Planara.Auth.Data;

public class DataContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<UserCredential> UserCredentials { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserCredential>()
            .HasKey(x => x.UserId);

        modelBuilder.Entity<UserCredential>()
            .HasIndex(x => x.Email)
            .IsUnique();

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(x => x.TokenHash)
            .IsUnique();

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(x => x.UserId);
        
        modelBuilder.Entity<OutboxMessage>()
            .HasKey(x => x.Id);
        
        modelBuilder.Entity<OutboxMessage>()
            .Property(x => x.PayloadJson)
            .IsRequired()
            .HasColumnType("jsonb");
        
        modelBuilder.Entity<OutboxMessage>()
            .HasIndex(x => x.ProcessedAt);
        
        modelBuilder.Entity<OutboxMessage>()
            .HasIndex(x => x.CreatedAt);

        modelBuilder.Entity<OutboxMessage>()
            .HasIndex(x => x.LockedUntil);

        modelBuilder.Entity<OutboxMessage>()
            .HasIndex(x => x.Type);

        modelBuilder.Entity<OutboxMessage>()
            .HasIndex(x => x.TopicKey);
    }
}