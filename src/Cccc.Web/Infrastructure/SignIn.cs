using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using Cccc.Store;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.HttpOverrides;

namespace Cccc.Web.Infrastructure;

/// <summary>The two claims a cookie carries: whose it is and the account's stamp when it was issued.</summary>
public static class AccountClaims
{
    public const string Id = "cccc:account";
    public const string Stamp = "cccc:stamp";

    public static ClaimsPrincipal For(Account a) => new(new ClaimsIdentity(
    [
        new Claim(Id, a.Id.ToString()),
        new Claim(Stamp, a.Stamp),
        new Claim(ClaimTypes.Name, a.Display),
    ], CookieAuthenticationDefaults.AuthenticationScheme));

    public static Guid? IdOf(ClaimsPrincipal? p) =>
        Guid.TryParse(p?.FindFirst(Id)?.Value, out var g) ? g : null;

    /// <summary>
    /// The account behind a principal, only if it still exists, can sign in, and its stamp has not moved
    /// (a passphrase, role or suspension change moves it, which signs the person out everywhere).
    /// </summary>
    public static Account? Current(ClaimsPrincipal? p, AccountDirectory accounts)
    {
        if (IdOf(p) is not { } id) return null;
        var a = accounts.Find(id);
        if (a is null || a.Effective == Role.Nobody) return null;
        return a.Stamp == p!.FindFirst(Stamp)?.Value ? a : null;
    }
}

/// <summary>Ten sign-in attempts a minute per client. A real budget, not a formality.</summary>
public sealed class SignInLimiter
{
    public const int AttemptsPerWindow = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _seen = new();

    public bool Allow(string client, DateTimeOffset now)
    {
        var q = _seen.GetOrAdd(client, _ => new Queue<DateTimeOffset>());
        lock (q)
        {
            while (q.Count > 0 && now - q.Peek() > Window) q.Dequeue();
            if (q.Count >= AttemptsPerWindow) return false;
            q.Enqueue(now);
            return true;
        }
    }
}

/// <summary>
/// A live circuit re-checks its account every minute, so a removal or suspension reaches a tab that
/// is simply left open.
/// </summary>
public sealed class AccountRevalidation(ILoggerFactory logs, IServiceScopeFactory scopes)
    : RevalidatingServerAuthenticationStateProvider(logs)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountDirectory>();
        return Task.FromResult(AccountClaims.Current(state.User, accounts) is not null);
    }
}

public static class SignInSetup
{
    public const string CookieName = "cccc.signin";

    public static void AddSignIn(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = CookieName;
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                // Secure exactly when the request is https. The school laptop runs plain http inside the
                // building; a Secure cookie there would lock everybody out with the right passphrase.
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(7);
                o.SlidingExpiration = false;
                o.LoginPath = "/signin";
                o.LogoutPath = "/signout";
                o.AccessDeniedPath = "/signin";
                o.ReturnUrlParameter = "back";
                o.Events.OnValidatePrincipal = async ctx =>
                {
                    var accounts = ctx.HttpContext.RequestServices.GetRequiredService<AccountDirectory>();
                    var a = AccountClaims.Current(ctx.Principal, accounts);
                    if (a is null)
                    {
                        ctx.RejectPrincipal();
                        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        return;
                    }
                    ctx.HttpContext.Items[typeof(Account)] = a;
                };
            });

        // Every page is behind sign-in by a FALLBACK policy, so a page added later is refused too.
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser().Build());

        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<AuthenticationStateProvider, AccountRevalidation>();
        builder.Services.AddSingleton<SignInLimiter>();

        var proxies = builder.Configuration["SignIn:TrustedProxies"];
        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownProxies.Clear();
            o.KnownIPNetworks.Clear();
            foreach (var p in (proxies ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // A half-configured proxy list is the failure that looks exactly like success: refuse.
                if (!IPAddress.TryParse(p, out var ip))
                    throw new InvalidOperationException($"SignIn:TrustedProxies has an entry that is not an IP address: '{p}'.");
                o.KnownProxies.Add(ip);
            }
        });
    }

    /// <summary>
    /// A person whose passphrase was set by an administrator chooses their own before anything else.
    /// </summary>
    public static IApplicationBuilder UseMustChangePassphrase(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        if (ctx.Items[typeof(Account)] is Account { MustChange: true }
            && !ctx.Request.Path.StartsWithSegments("/passphrase")
            && !ctx.Request.Path.StartsWithSegments("/signout")
            && !ctx.Request.Path.StartsWithSegments("/_framework")
            && !ctx.Request.Path.StartsWithSegments("/css")
            && !ctx.Request.Path.StartsWithSegments("/js"))
        {
            ctx.Response.Redirect("/passphrase");
            return;
        }
        await next();
    });

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var h = ctx.Response.Headers;
        h["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
            "connect-src 'self' ws: wss:; font-src 'self'; object-src 'none'; base-uri 'self'; " +
            "form-action 'self'; frame-ancestors 'none'";
        h["X-Content-Type-Options"] = "nosniff";
        h["Referrer-Policy"] = "no-referrer";
        h["X-Frame-Options"] = "DENY";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        await next();
    });

    public static string ClientOf(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
