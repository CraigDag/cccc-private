using Cccc.Domain.Rules;
using Cccc.Store;

namespace Cccc.Web.Infrastructure;

/// <summary>
/// What the signed-in person may do. Capabilities are named, never <c>role &gt;= Editor</c>, so a role
/// this app has no name for cannot inherit a power by being numerically large. Fail-closed: no account
/// means nothing.
/// </summary>
public sealed record Access(Account? Account)
{
    public static Access Nobody { get; } = new((Account?)null);

    public Role Role => Account?.Effective ?? Role.Nobody;

    public bool SignedIn => Role is Role.Viewer or Role.Editor or Role.Administrator;

    public bool MayEdit => Role is Role.Editor or Role.Administrator;

    /// <summary>Anybody signed in can change something: a facilitator files their own registers.</summary>
    public bool MayEditSomething => SignedIn;

    public bool MayUseAdminTools => Role is Role.Administrator;

    public bool MayManageAccounts => Role is Role.Administrator;

    public bool MayManageProjects => Role is Role.Administrator;

    public bool SeesAdministratorsEvents => Role is Role.Administrator;

    public bool MayTakeEveryRegister => Role is Role.Administrator;

    public bool MaySeeSystem => Role is Role.Administrator;

    /// <summary>The name changes are recorded under: the login, which is unique.</summary>
    public string By => Account?.Login ?? "";

    public string Display => Account?.Display ?? "";

    public Guid? FacilitatorId => Account?.FacilitatorId;

    public RegisterReader Registers => new(MayTakeEveryRegister, SignedIn ? FacilitatorId : null);

    public EventViewer Events => !SignedIn ? EventViewer.Nobody : new(
        SeesAdministratorsEvents, MayEdit,
        Account!.Tags.HasFlag(AccountTags.Student), FacilitatorId is not null,
        Account.Tags.HasFlag(AccountTags.Staff));

    /// <summary>Who may print whose week: an administrator anybody's, everybody else their own.</summary>
    public bool MayHaveTheWeekOf(Guid facilitatorId) =>
        MayUseAdminTools || (SignedIn && FacilitatorId == facilitatorId);
}
