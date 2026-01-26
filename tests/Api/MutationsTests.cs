using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planara.Auth.Data.Domain;
using Planara.Auth.Services;

namespace Planara.Auth.Tests.Api;

public class MutationsTests: BaseApiTest
{
    public MutationsTests(ApiTestWebAppFactory factory) : base(factory) { }

    private async Task ResetAsync()
        => await DbTestUtils.ResetAuthDbAsync(Context);

    [Fact]
    public async Task Register_Success_ReturnsTokens_AndPersistsUserAndRefreshToken()
    {
        await ResetAsync();

        const string mutation = """
        mutation ($request: RegisterRequestInput!) {
          register(request: $request) {
            accessToken
            refreshToken
            accessExpiresAtUtc
            refreshExpiresAtUtc
          }
        }
        """;

        var doc = await Client.PostAsync(mutation, new
        {
            request = new { email = "  TEST@Example.COM ", password = "Qwerty1!" }
        });

        doc.GetErrors().Should().BeNull();

        var data = doc.GetData().GetProperty("register");
        data.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();

        // DB: email нормализован, юзер создан
        var cred = await Context.UserCredentials.SingleAsync();
        cred.Email.Should().Be("test@example.com");

        // DB: refresh token создан
        (await Context.RefreshTokens.CountAsync()).Should().Be(1);
        var rt = await Context.RefreshTokens.SingleAsync();
        rt.UserId.Should().Be(cred.UserId);
        rt.RevokedAtUtc.Should().BeNull();
        rt.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsGraphQlError()
    {
        await ResetAsync();

        // seed
        Context.UserCredentials.Add(new UserCredential
        {
            UserId = Guid.NewGuid(),
            Email = "dup@example.com",
            PasswordHash = "hash"
        });
        await Context.SaveChangesAsync();

        const string mutation = """
        mutation ($request: RegisterRequestInput!) {
          register(request: $request) { accessToken }
        }
        """;

        var doc = await Client.PostAsync(mutation, new
        {
            request = new { email = "dup@example.com", password = "Qwerty1!" }
        });

        var errors = doc.GetErrors();
        errors.Should().NotBeNull();
        errors.Value[0].GetProperty("message").GetString().Should().Be("Email already registered");
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsError()
    {
        await ResetAsync();

        var userId = Guid.NewGuid();
        Context.UserCredentials.Add(new UserCredential
        {
            UserId = userId,
            Email = "a@b.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Right1!", workFactor: 12)
        });
        await Context.SaveChangesAsync();

        const string mutation = """
        mutation ($login: LoginRequestInput!) {
          login(login: $login) { accessToken }
        }
        """;

        var doc = await Client.PostAsync(mutation, new
        {
            login = new { email = "a@b.com", password = "Wrong1!" }
        });

        doc.GetErrors().Should().NotBeNull();
    }
    
    [Fact]
    public async Task Login_UserNotFound_ReturnsError()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        const string mutation = """
        mutation ($login: LoginRequestInput!) {
          login(login: $login) {
            accessToken
          }
        }
        """;

        var doc = await Client.PostAsync(mutation, new
        {
            login = new { email = "missing@x.com", password = "Qwerty1!" }
        });

        doc.GetErrors().Should().NotBeNull();
    }

    [Fact]
    public async Task Login_Success_ReturnsTokens_AndStoresRefreshToken()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var userId = Guid.NewGuid();
        Context.UserCredentials.Add(new UserCredential
        {
            UserId = userId,
            Email = "a@b.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Right1!", workFactor: 12)
        });
        await Context.SaveChangesAsync();

        const string mutation = """
        mutation ($login: LoginRequestInput!) {
          login(login: $login) {
            accessToken
            refreshToken
            accessExpiresAtUtc
            refreshExpiresAtUtc
          }
        }
        """;

        var doc = await Client.PostAsync(mutation, new
        {
            login = new { email = "  A@B.COM ", password = "Right1!" }
        });

        doc.GetErrors().Should().BeNull();

        var data = doc.GetData().GetProperty("login");
        data.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();

        var rts = await Context.RefreshTokens.Where(x => x.UserId == userId).ToListAsync();
        rts.Should().HaveCount(1);
        rts[0].RevokedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Refresh_Success_RotatesRefreshToken_AndRevokesOld()
    {
        await ResetAsync();

        // 1) Register -> получаем access+refresh
        const string register = """
        mutation ($request: RegisterRequestInput!) {
          register(request: $request) {
            accessToken
            refreshToken
          }
        }
        """;

        var reg = await Client.PostAsync(register, new
        {
            request = new { email = "r@r.com", password = "Qwerty1!" }
        });

        reg.GetErrors().Should().BeNull();
        var regData = reg.GetData().GetProperty("register");
        var access = regData.GetProperty("accessToken").GetString()!;
        var oldRefreshRaw = regData.GetProperty("refreshToken").GetString()!;

        // 2) Refresh требует [Authorize] -> шлём Bearer access
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", access);

        const string refresh = """
        mutation ($request: RefreshRequestInput!) {
          refresh(request: $request) {
            accessToken
            refreshToken
          }
        }
        """;

        var doc = await Client.PostAsync(refresh, new
        {
            request = new { refreshToken = oldRefreshRaw }
        });

        doc.GetErrors().Should().BeNull();

        var data = doc.GetData().GetProperty("refresh");
        var newRefreshRaw = data.GetProperty("refreshToken").GetString()!;
        newRefreshRaw.Should().NotBeNullOrWhiteSpace();
        newRefreshRaw.Should().NotBe(oldRefreshRaw);

        // DB: старый токен отозван, новый добавлен
        var all = await Context.RefreshTokens.OrderBy(x => x.CreatedAtUtc).ToListAsync();
        all.Count.Should().Be(2);

        all[0].RevokedAtUtc.Should().NotBeNull();
        all[0].ReplacedByTokenHash.Should().NotBeNullOrWhiteSpace();

        all[1].RevokedAtUtc.Should().BeNull();
    }
    
    [Fact]
    public async Task Refresh_InvalidToken_ReturnsError()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var (access, _) = await RegisterAndGetAccessAsync("inv@t.com");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);

        const string refresh = """
        mutation ($request: RefreshRequestInput!) {
          refresh(request: $request) { accessToken }
        }
        """;

        var doc = await Client.PostAsync(refresh, new
        {
            request = new { refreshToken = "not-a-real-token" }
        });

        doc.GetErrors().Should().NotBeNull();
        doc.GetErrors()!.Value[0].GetProperty("message").GetString().Should().Be("Invalid refresh token");
    }

    [Fact]
    public async Task Refresh_RevokedToken_ReturnsError()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var (access, userId) = await RegisterAndGetAccessAsync("rev@t.com");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);

        using var scope = Factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var raw = "raw-refresh";
        var hash = tokenService.HashRefreshToken(raw);

        Context.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = hash,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
            RevokedAtUtc = DateTime.UtcNow.AddMinutes(-1) // already revoked
        });
        await Context.SaveChangesAsync();

        const string refresh = """
        mutation ($request: RefreshRequestInput!) {
          refresh(request: $request) { accessToken }
        }
        """;

        var doc = await Client.PostAsync(refresh, new
        {
            request = new { refreshToken = raw }
        });

        doc.GetErrors().Should().NotBeNull();
        doc.GetErrors()!.Value[0].GetProperty("message").GetString().Should().Be("Refresh token revoked");
    }

    [Fact]
    public async Task Refresh_ExpiredToken_ReturnsError()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var (access, userId) = await RegisterAndGetAccessAsync("exp@t.com");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);

        using var scope = Factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var raw = "raw-expired";
        var hash = tokenService.HashRefreshToken(raw);

        Context.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = hash,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-40),
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1), // expired
            RevokedAtUtc = null
        });
        await Context.SaveChangesAsync();

        const string refresh = """
        mutation ($request: RefreshRequestInput!) {
          refresh(request: $request) { accessToken }
        }
        """;

        var doc = await Client.PostAsync(refresh, new
        {
            request = new { refreshToken = raw }
        });

        doc.GetErrors().Should().NotBeNull();
        doc.GetErrors()!.Value[0].GetProperty("message").GetString().Should().Be("Refresh token expired");
    }

    [Fact]
    public async Task Logout_SetsRevokedAtUtc_AndIsIdempotent()
    {
        await ResetAsync();

        // Register
        const string register = """
        mutation ($request: RegisterRequestInput!) {
          register(request: $request) {
            accessToken
            refreshToken
          }
        }
        """;

        var reg = await Client.PostAsync(register, new
        {
            request = new { email = "l@l.com", password = "Qwerty1!" }
        });

        var regData = reg.GetData().GetProperty("register");
        var access = regData.GetProperty("accessToken").GetString()!;
        var refreshRaw = regData.GetProperty("refreshToken").GetString()!;

        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", access);

        const string logout = """
        mutation ($request: LogoutRequestInput!) {
          logout(request: $request) { success }
        }
        """;

        // first
        var doc1 = await Client.PostAsync(logout, new
        {
            request = new { refreshToken = refreshRaw }
        });

        doc1.GetErrors().Should().BeNull();
        doc1.GetData().GetProperty("logout").GetProperty("success").GetBoolean().Should().BeTrue();

        (await Context.RefreshTokens.SingleAsync()).RevokedAtUtc.Should().NotBeNull();

        // second (idempotent)
        var doc2 = await Client.PostAsync(logout, new
        {
            request = new { refreshToken = refreshRaw }
        });

        doc2.GetErrors().Should().BeNull();
        doc2.GetData().GetProperty("logout").GetProperty("success").GetBoolean().Should().BeTrue();
    }
    
    private async Task<(string Access, Guid UserId)> RegisterAndGetAccessAsync(string email = "x@y.com")
    {
        const string register = """
                                mutation ($request: RegisterRequestInput!) {
                                  register(request: $request) {
                                    accessToken
                                  }
                                }
                                """;

        var doc = await Client.PostAsync(register, new
        {
            request = new { email, password = "Qwerty1!" }
        });

        doc.GetErrors().Should().BeNull();

        var access = doc.GetData().GetProperty("register").GetProperty("accessToken").GetString()!;
        var userId = (await Context.UserCredentials.SingleAsync(x => x.Email == email)).UserId;

        return (access, userId);
    }
}