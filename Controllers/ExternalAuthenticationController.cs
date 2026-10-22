using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Client.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthServiceProto.Controllers;

// Получает уже проверенный OpenIddict ответ Яндекса и передаёт его встроенной странице Identity.
public sealed class ExternalAuthenticationController : Controller
{
    [HttpGet("~/callback/login/{provider}")]
    [HttpPost("~/callback/login/{provider}")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Callback()
    {
        // Ответ провайдера (state, code, подписи/nonce где применимо) проверяет OpenIddict Client.
        // Контроллер принимает только уже аутентифицированный principal.
        var result = await HttpContext.AuthenticateAsync(
            OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);

        if (result is not { Succeeded: true, Principal.Identity.IsAuthenticated: true })
        {
            throw new InvalidOperationException("The external login response is invalid.");
        }

        var identifier = result.Principal.GetClaim(ClaimTypes.NameIdentifier)
            ?? result.Principal.GetClaim(Claims.Subject)
            ?? throw new InvalidOperationException("The external provider did not return a user identifier.");

        var identity = new ClaimsIdentity(
            IdentityConstants.ExternalScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, identifier));

        CopyClaim(result.Principal, identity, ClaimTypes.Email);
        CopyClaim(result.Principal, identity, ClaimTypes.Name);

        var returnUrl = result.Properties.RedirectUri;
        var properties = new AuthenticationProperties(result.Properties.Items)
        {
            // После входа разрешаем возврат только на страницу нашего приложения,
            // чтобы адрес перехода нельзя было подменить ссылкой на чужой сайт.
            RedirectUri = Url.IsLocalUrl(returnUrl) ? returnUrl : "/",
            IssuedUtc = null,
            ExpiresUtc = null,
            IsPersistent = false
        };

        return SignIn(new ClaimsPrincipal(identity), properties, IdentityConstants.ExternalScheme);
    }

    private static void CopyClaim(ClaimsPrincipal source, ClaimsIdentity destination, string type)
    {
        var value = source.GetClaim(type);
        if (!string.IsNullOrWhiteSpace(value))
        {
            destination.AddClaim(new Claim(type, value));
        }
    }
}
