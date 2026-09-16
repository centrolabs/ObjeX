using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ObjeX.Api.Options;
using ObjeX.Core.Models;

namespace ObjeX.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        app.MapPost("/account/login", async (HttpContext ctx, SignInManager<User> signInManager, IOptions<AuthOptions> auth, ILogger<User> logger) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var login = form["login"].ToString();
            var password = form["password"].ToString();
            var returnUrl = form["returnUrl"].ToString();
            var rememberMe = form["rememberMe"] == "true";
            ctx.Response.Cookies.Append("objex-remember", rememberMe ? "1" : "0",
                new CookieOptions { MaxAge = TimeSpan.FromDays(365), HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = ctx.Request.IsHttps, IsEssential = true });
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var sanitizedLogin = login.Replace("\r", "").Replace("\n", "");

            string LoginRedirect(string? message)
            {
                var qs = $"error=1&login={Uri.EscapeDataString(login)}";
                if (message is not null) qs += $"&msg={Uri.EscapeDataString(message)}";
                if (!string.IsNullOrEmpty(returnUrl)) qs += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
                return $"/login?{qs}";
            }

            var user = login.Contains('@')
                ? await signInManager.UserManager.FindByEmailAsync(login)
                : await signInManager.UserManager.FindByNameAsync(login);

            if (user is not null)
            {
                // lockoutOnFailure: failed attempts count against the account (Auth:Lockout in config).
                var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);

                if (result.IsLockedOut)
                {
                    var lockoutEnd = await signInManager.UserManager.GetLockoutEndDateAsync(user);
                    var remaining = lockoutEnd.HasValue ? lockoutEnd.Value - DateTimeOffset.UtcNow : TimeSpan.Zero;
                    var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));

                    logger.LogWarning("Login refused for locked-out account {Login} from {IP}", sanitizedLogin, ip);
                    return Results.Redirect(LoginRedirect(
                        $"Too many failed attempts. This account is locked for {minutes} more minute{(minutes == 1 ? "" : "s")}."));
                }

                if (result.Succeeded)
                {
                    if (user.IsDeactivated)
                        return Results.Redirect(LoginRedirect("Your account has been deactivated."));

                    if (user.MustChangePassword
                        && user.TemporaryPasswordExpiresAt.HasValue
                        && user.TemporaryPasswordExpiresAt.Value < DateTime.UtcNow)
                        return Results.Redirect(LoginRedirect("Temporary password expired, contact your administrator."));

                    // Sliding renewal reuses ExpiresUtc - IssuedUtc, so a remembered ticket keeps its RememberMeDays lifetime.
                    await signInManager.SignInAsync(user, new AuthenticationProperties
                    {
                        IsPersistent = rememberMe,
                        ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(auth.Value.RememberMeDays) : null
                    });

                    if (user.MustChangePassword)
                        return Results.Redirect("/change-password");

                    var safeUrl = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
                        ? returnUrl : "/";
                    return Results.Redirect(safeUrl);
                }
            }

            logger.LogWarning("Failed login attempt for {Login} from {IP}", sanitizedLogin, ip);
            return Results.Redirect(LoginRedirect(null));
        }).DisableAntiforgery(); // Login.razor uses a plain HTML form (not a Blazor form) — antiforgery token generation from static SSR is non-trivial. Login CSRF is low impact (attacker can only log the victim into the attacker's account). Account lockout limits abuse.

        app.MapGet("/account/logout", async (SignInManager<User> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.Redirect("/login");
        });
    }
}
