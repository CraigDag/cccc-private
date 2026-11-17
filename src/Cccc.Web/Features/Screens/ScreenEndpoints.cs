using Cccc.Store;
using Cccc.Web.Infrastructure;

namespace Cccc.Web.Features.Screens;

/// <summary>
/// The cookie a paired screen holds, and where a waiting screen collects it. The cookie is sent only to
/// /screen (its path), so it never reaches a page behind sign-in, and it is not a sign-in: the fallback
/// policy refuses every other page to a screen exactly as it refuses anybody not signed in.
/// </summary>
public static class ScreenEndpoints
{
    public const string CookieName = "cccc.screen";
    public const string Path = "/screen";

    /// <summary>The longest a browser keeps a cookie; each look at the screen starts it again.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromDays(400);

    public static void Give(HttpContext ctx, string token, DateTimeOffset now) =>
        ctx.Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            Path = Path, HttpOnly = true, SameSite = SameSiteMode.Strict, IsEssential = true,
            // Secure exactly when the request is https: the school laptop serves plain http inside the building.
            Secure = ctx.Request.IsHttps, Expires = now + Keep,
        });

    public static void TakeBack(HttpContext ctx) =>
        ctx.Response.Cookies.Delete(CookieName, new CookieOptions { Path = Path, HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = ctx.Request.IsHttps });

    public static void MapScreens(this WebApplication app)
    {
        // A waiting screen asks every few seconds whether it has been paired. The claim is the secret its own
        // page was given with its code; nobody else holds it, so nobody else can collect the screen's token.
        app.MapPost("/screen/collect", async (HttpContext ctx, ScreenPairing pairing, SchoolClock clock) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var buffer = new char[256];
            using var reader = new StreamReader(ctx.Request.Body);
            int read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
            var (outcome, token) = pairing.Collect(new string(buffer, 0, read), clock.Now);
            switch (outcome)
            {
                case ScreenPairing.Outcome.Paired:
                    Give(ctx, token!, clock.Now);
                    return Results.Json(new { paired = true });
                case ScreenPairing.Outcome.Pending:
                    return Results.Json(new { paired = false }, statusCode: StatusCodes.Status202Accepted);
                default:
                    // With a body, so the not-found page is not run in its place: run for a POST, that page
                    // answers 400, and the screen would never learn to fetch a new code.
                    return Results.Json(new { paired = false }, statusCode: StatusCodes.Status410Gone);
            }
        }).AllowAnonymous().DisableAntiforgery();
    }
}

/// <summary>New pairing codes, ten a minute for each address, so a page reloaded in a loop cannot fill the list.</summary>
public sealed class ScreenLimiter
{
    private readonly SignInLimiter _inner = new();

    public bool Allow(string client, DateTimeOffset now) => _inner.Allow(client, now);
}
