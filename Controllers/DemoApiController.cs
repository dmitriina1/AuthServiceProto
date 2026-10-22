using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthServiceProto.Controllers;

// Два условных сервиса доказывают, что токен для Orders нельзя использовать в Profile и наоборот.
[ApiController]
[Route("api")]
public sealed class DemoApiController : ControllerBase
{
    [HttpGet("orders/me")]
    [Authorize(Policy = AuthConstants.OrdersPolicy,
        AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
    public IActionResult GetOrders()
    {
        return Ok(new
        {
            service = "orders-api",
            subject = User.GetClaim(Claims.Subject),
            message = "The token is valid only for the orders audience."
        });
    }

    [HttpGet("profile/me")]
    [Authorize(Policy = AuthConstants.ProfilePolicy,
        AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
    public IActionResult GetProfile()
    {
        return Ok(new
        {
            service = "profile-api",
            subject = User.GetClaim(Claims.Subject),
            name = User.GetClaim(Claims.Name),
            email = User.GetClaim(Claims.Email)
        });
    }
}
