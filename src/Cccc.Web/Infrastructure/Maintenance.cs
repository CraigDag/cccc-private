using Cccc.Domain.Sample;
using Cccc.Store;

namespace Cccc.Web.Infrastructure;

/// <summary>Once an hour: fold history older than thirty days, and take snapshots of what has moved.</summary>
public sealed class Maintenance(ProjectStore store, LiveProjects live, SchoolClock clock, ILogger<Maintenance> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                live.SettleAll();
                store.Prune(clock.Now);
                store.SetMeta("last_prune", clock.Now.ToString("O"));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Hourly maintenance failed");
            }
        } while (await timer.WaitForNextTickAsync(stop));
    }
}

/// <summary>
/// Made-up data for a preview, and nothing else: the fictional Northlight school and three preview
/// accounts whose passphrases are written in the README on purpose. Development only.
/// </summary>
public static class Preview
{
    public static readonly (string Login, string Display, Role Role, string Passphrase)[] Accounts =
    [
        ("admin", "Preview Administrator", Role.Administrator, "northlight admin"),
        ("editor", "Preview Editor", Role.Editor, "northlight editor"),
        ("coach", "Ada Brightwater", Role.Viewer, "northlight coach"),
    ];

    public static void Seed(ProjectStore store, AccountDirectory accounts, SchoolClock clock, bool withAccounts)
    {
        if (store.List(includeDeleted: true).Count == 0)
            store.Create("Northlight 2026–27 (sample)", SampleSchool.Create(clock.Today, clock.Today), "admin", clock.Now);

        if (!withAccounts || accounts.Any()) return;
        var first = Accounts[0];
        var admin = accounts.CreateFirstAdministrator(accounts.SetupToken()!, first.Login, first.Display, first.Passphrase);
        var t = store.Load(store.Landing()!.Id).State;
        var ada = t.Facilitators.FirstOrDefault(f => f.Name.StartsWith("Ada"));
        foreach (var (login, display, role, pass) in Accounts.Skip(1))
        {
            var a = accounts.Add(admin.Id, login, display, role, pass);
            accounts.Update(admin.Id, a.Id, display, role, AccountTags.Staff, role == Role.Viewer ? ada?.Id : null, false);
            // Preview accounts start ready to use; a real one added by an administrator must change.
            accounts.ClearMustChange(a.Id);
        }
    }
}
