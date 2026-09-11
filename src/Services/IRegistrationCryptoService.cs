namespace Planara.Auth.Services;

public interface IRegistrationCryptoService
{
    string GenerateCode();

    string GenerateSessionToken();

    string HashCode(string code);

    bool VerifyCode(string code, string hash);

   string HashSessionToken(string token);

    bool VerifySessionToken(string token, string hash);
}