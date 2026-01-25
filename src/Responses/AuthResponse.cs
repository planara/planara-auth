namespace Planara.Auth.Responses;

public class AuthResponse
{
    public string AccessToken { get; set; } = null!;
    public DateTime AccessExpiresAtUtc { get; set; }
    public string RefreshToken { get; set; } = null!;
    public DateTime RefreshExpiresAtUtc { get; set; }
}