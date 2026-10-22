using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthServiceProto.Controllers;

// Связывает пользователя Identity с OpenIddict: обрабатывает вход, обмен кода на токены и выход.
public sealed class AuthorizationController(
    UserManager<IdentityUser> userManager,
    SignInManager<IdentityUser> signInManager,
    IOpenIddictScopeManager scopeManager) : Controller
{
    [HttpGet("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    // Шаг 1: браузер направляет пользователя на вход и получает одноразовый код авторизации.
    public async Task<IActionResult> AuthorizeEndpoint()
    {
        // OpenIddict уже проверил client_id, redirect_uri, response_type и PKCE-параметры.
        // Здесь мы связываем валидный протокольный запрос с локальной Identity-сессией.
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be read.");

        var cookie = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var requiresFreshLogin = request.HasPromptValue(PromptValues.Login) ||
            request.MaxAge is not null &&
            (cookie.Properties?.IssuedUtc is null ||
             DateTimeOffset.UtcNow - cookie.Properties.IssuedUtc > TimeSpan.FromSeconds(request.MaxAge.Value));

        if (!cookie.Succeeded || requiresFreshLogin)
        {
            if (request.HasPromptValue(PromptValues.None) && !request.HasPromptValue(PromptValues.Login))
            {
                return Forbid(
                    new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.LoginRequired,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "The user is not logged in."
                    }),
                    OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            if (cookie.Succeeded)
            {
                await signInManager.SignOutAsync();
            }

            return Challenge(new AuthenticationProperties
            {
                RedirectUri = RemovePromptLogin(Request)
            });
        }

        var user = await userManager.GetUserAsync(cookie.Principal!)
            ?? throw new InvalidOperationException("The signed-in user no longer exists.");
        if (!await signInManager.CanSignInAsync(user) || await userManager.IsLockedOutAsync(user))
        {
            await signInManager.SignOutAsync();
            return Challenge(new AuthenticationProperties { RedirectUri = RemovePromptLogin(Request) });
        }

        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            Claims.Name,
            Claims.Role);

        identity.SetClaim(Claims.Subject, await userManager.GetUserIdAsync(user))
            .SetClaim(Claims.Email, await userManager.GetEmailAsync(user))
            .SetClaim(Claims.Name, await userManager.GetUserNameAsync(user))
            .SetClaim(Claims.PreferredUsername, await userManager.GetUserNameAsync(user))
            .SetClaim(AuthConstants.SecurityStampClaim, await userManager.GetSecurityStampAsync(user));
        identity.SetScopes(request.GetScopes());

        var resources = new List<string>();
        await foreach (var resource in scopeManager.ListResourcesAsync(identity.GetScopes()))
        {
            resources.Add(resource);
        }

        if (resources.Count > 1)
        {
            // Один access token предназначен ровно одному API. Это не даёт токену
            // orders-api случайно стать универсальным пропуском во все сервисы.
            return Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidScope,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                        "Request exactly one service scope per access token."
                }),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        identity.SetResources(resources);
        identity.SetDestinations(GetDestinations);

        return SignIn(
            new ClaimsPrincipal(identity),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("~/connect/token")]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    // Шаг 2: клиент обменивает код и подтверждение PKCE на токены либо обновляет токен.
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be read.");

        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            throw new InvalidOperationException("Only authorization_code and refresh_token are supported.");
        }

        var authentication = await HttpContext.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var subject = authentication.Principal?.GetClaim(Claims.Subject);
        var user = subject is null ? null : await userManager.FindByIdAsync(subject);

        if (user is null ||
            !await signInManager.CanSignInAsync(user) ||
            await userManager.IsLockedOutAsync(user))
        {
            return Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                        "The user account is no longer available."
                }),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var tokenSecurityStamp = authentication.Principal?.GetClaim(AuthConstants.SecurityStampClaim);
        var currentSecurityStamp = await userManager.GetSecurityStampAsync(user);
        // SecurityStamp - служебная контрольная метка учётной записи Identity.
        // После смены пароля или принудительного сброса сеансов она обновляется,
        // поэтому выданные ранее коды авторизации и токены для обновления.
        if (string.IsNullOrEmpty(tokenSecurityStamp) ||
            !string.Equals(tokenSecurityStamp, currentSecurityStamp, StringComparison.Ordinal))
        {
            return Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                        "The authorization is no longer valid."
                }),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var identity = new ClaimsIdentity(
            authentication.Principal!.Claims,
            TokenValidationParameters.DefaultAuthenticationType,
            Claims.Name,
            Claims.Role);

        identity.SetClaim(Claims.Subject, await userManager.GetUserIdAsync(user))
            .SetClaim(Claims.Email, await userManager.GetEmailAsync(user))
            .SetClaim(Claims.Name, await userManager.GetUserNameAsync(user))
            .SetClaim(Claims.PreferredUsername, await userManager.GetUserNameAsync(user))
            .SetClaim(AuthConstants.SecurityStampClaim, currentSecurityStamp);
        identity.SetDestinations(GetDestinations);

        return SignIn(
            new ClaimsPrincipal(identity),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpGet("~/connect/logout")]
    public IActionResult Logout() => View();

    [ActionName(nameof(Logout))]
    [HttpPost("~/connect/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LogoutPost()
    {
        // Удаляем локальную Identity cookie и просим OpenIddict завершить OIDC-сессию клиента.
        await signInManager.SignOutAsync();

        return SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        // Указываем, какие сведения можно включить в выдаваемые токены.
        if (claim.Type is "AspNet.Identity.SecurityStamp" or AuthConstants.SecurityStampClaim)
        {
            yield break;
        }

        if (claim.Type is Claims.Email && !claim.Subject!.HasScope(Scopes.Email) ||
            claim.Type is Claims.Name or Claims.PreferredUsername && !claim.Subject!.HasScope(Scopes.Profile))
        {
            yield break;
        }

        yield return Destinations.AccessToken;

        if (claim.Type is Claims.Subject ||
            claim.Type is Claims.Name or Claims.PreferredUsername && claim.Subject!.HasScope(Scopes.Profile) ||
            claim.Type is Claims.Email && claim.Subject!.HasScope(Scopes.Email))
        {
            yield return Destinations.IdentityToken;
        }
    }

    private static string RemovePromptLogin(HttpRequest request)
    {
        if (!request.Query.TryGetValue(Parameters.Prompt, out var prompt))
        {
            // После входа Identity возвращает пользователя только по внутреннему адресу.
            // Сохраняем путь и параметры без домена, чтобы исключить переход на чужой сайт.
            return request.PathBase + request.Path + request.QueryString;
        }

        var remaining = prompt
            .SelectMany(value => (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(value => !string.Equals(value, PromptValues.Login, StringComparison.Ordinal))
            .ToArray();
        var parameters = request.Query.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (remaining.Length == 0)
        {
            parameters.Remove(Parameters.Prompt);
        }
        else
        {
            parameters[Parameters.Prompt] = new StringValues(string.Join(' ', remaining));
        }

        return request.PathBase + request.Path + QueryString.Create(parameters);
    }
}
