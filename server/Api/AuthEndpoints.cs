using System.Security.Claims;
using MailGester.Auth;
using Microsoft.AspNetCore.Authentication;

namespace MailGester.Api;

public static class AuthEndpoints
{
    public const string Scheme = "mailgester";

    private sealed record PasswordRequest(string? Password);
    private sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    public static void MapAuth(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth");

        group.MapGet("/state", async (HttpContext http, AdminAuth auth, CancellationToken ct) =>
            Results.Ok(new
            {
                setupRequired = !await auth.HasPasswordAsync(ct),
                authenticated = http.User.Identity?.IsAuthenticated == true
            }));

        group.MapPost("/setup", async (PasswordRequest request, HttpContext http, AdminAuth auth, CancellationToken ct) =>
        {
            if (await auth.HasPasswordAsync(ct))
            {
                return Results.Conflict(new { error = "A password is already set." });
            }

            if (request.Password is not { Length: >= AdminAuth.MinLength })
            {
                return Results.BadRequest(new { error = $"Use at least {AdminAuth.MinLength} characters." });
            }

            await auth.SetPasswordAsync(request.Password, ct);
            await SignInAsync(http);
            return Results.NoContent();
        });

        group.MapPost("/login", async (PasswordRequest request, HttpContext http, AdminAuth auth, LoginThrottle throttle, CancellationToken ct) =>
        {
            var client = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (throttle.IsBlocked(client))
            {
                return Results.Json(new { error = "Too many attempts. Wait a minute." }, statusCode: StatusCodes.Status429TooManyRequests);
            }

            if (string.IsNullOrEmpty(request.Password) || !await auth.VerifyAsync(request.Password, ct))
            {
                throttle.Failed(client);
                return Results.Json(new { error = "Wrong password." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            throttle.Succeeded(client);
            await SignInAsync(http);
            return Results.NoContent();
        });

        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(Scheme);
            return Results.NoContent();
        });

        group.MapPost("/password", async (ChangePasswordRequest request, AdminAuth auth, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(request.CurrentPassword) || !await auth.VerifyAsync(request.CurrentPassword, ct))
            {
                return Results.Json(new { error = "The current password is wrong." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (request.NewPassword is not { Length: >= AdminAuth.MinLength })
            {
                return Results.BadRequest(new { error = $"Use at least {AdminAuth.MinLength} characters." });
            }

            await auth.SetPasswordAsync(request.NewPassword, ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static Task SignInAsync(HttpContext http)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], Scheme);
        return http.SignInAsync(Scheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
    }
}
