using System.Security.Claims;
using System.Text.Encodings.Web;
using MindBot.Core.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MindBot.Bot.Web;

/// <summary>Authenticates <c>Authorization: Bearer mbt_...</c> against the device token store.</summary>
public sealed class DeviceTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IDeviceTokenStore deviceTokens) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "DeviceToken";

    public const string DeviceIdClaim = "mindbot:device-id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var device = await deviceTokens.AuthenticateAsync(header["Bearer ".Length..].Trim(), Context.RequestAborted);
        if (device is null)
        {
            // Deliberately vague, and never echoes the token: the formatter would redact it, but
            // there is no reason to hand it to the log at all.
            return AuthenticateResult.Fail("Unknown or revoked device token.");
        }

        return AuthenticateResult.Success(new AuthenticationTicket(CreatePrincipal(device), SchemeName));
    }

    public static ClaimsPrincipal CreatePrincipal(DeviceInfo device) =>
        new(new ClaimsIdentity(
            [
                new Claim(DeviceIdClaim, device.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Name, device.Name),
            ],
            SchemeName));

    public static long DeviceId(ClaimsPrincipal user) =>
        long.Parse(user.FindFirstValue(DeviceIdClaim)!, System.Globalization.CultureInfo.InvariantCulture);
}
