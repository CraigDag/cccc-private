using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Cccc.Web.Features.SignIn;

public static class SignInEndpoints
{
    public static void MapSignInEndpoints(this WebApplication app)
    {
        app.MapPost("/signout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/signin");
        }).AllowAnonymous();
    }
}
