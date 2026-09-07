using System.Security.Cryptography;
using System.Text;

namespace Planara.Auth.Services;

public sealed class RegistrationCryptoService(IConfiguration configuration) : IRegistrationCryptoService
{
    private const string CodePurpose = "registration-code:";
    private const string SessionPurpose = "registration-session:";

    private readonly byte[] _secret = Convert.FromBase64String(
        configuration["Registration:CryptoSecret"]
        ?? throw new InvalidOperationException(
            "Registration crypto secret is not configured.")
    );

    public string GenerateCode() => RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4");

    public string GenerateSessionToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public string HashCode(string code) => Hash(CodePurpose, code);

    public bool VerifyCode(string code, string hash) => Verify(CodePurpose, code, hash);

    public string HashSessionToken(string token) =>  Hash(SessionPurpose, token);

    public bool VerifySessionToken(string token, string hash) => Verify(SessionPurpose, token, hash);

    private string Hash(string purpose, string value)
    {
        using var hmac = new HMACSHA256(_secret);

        var payload = Encoding.UTF8.GetBytes(purpose + value);

        return Convert.ToHexString(hmac.ComputeHash(payload));
    }

    private bool Verify(string purpose, string value, string expectedHash)
    {
        byte[] expected;

        try
        {
            expected = Convert.FromHexString(expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Convert.FromHexString(Hash(purpose, value));

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}