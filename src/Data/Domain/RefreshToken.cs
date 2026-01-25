using Planara.Common.Database.Domain;

namespace Planara.Auth.Data.Domain;

public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }
    
    public required string TokenHash { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    
    public string? ReplacedByTokenHash { get; set; }
    
    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }
}
