using Microsoft.EntityFrameworkCore;
using Planara.Auth.Data.Domain;

namespace Planara.Auth.Data;

public class DataContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<UserCredential> UserCredentials { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    
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
    }
}