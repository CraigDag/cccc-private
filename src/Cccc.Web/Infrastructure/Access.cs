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

    /// <summary>
    /// The open timetable follows the current app (<see cref="Follower"/>), so nothing in it is changed
    /// here, by anybody: a change made here would be undone by the next save over there. It narrows what
    /// a person may CHANGE and nothing they may SEE: an editor still sees what editors see, and whose
    /// registers are outstanding is still counted, because they are still outstanding over there.
    /// </summary>
    public bool Following { get; init; }

    public bool SignedIn => Role is Role.Viewer or Role.Editor or Role.Administrator;

    /// <summary>The role's own standing, for what it SEES; <see cref="MayEdit"/> is what it may change here.</summary>
    private bool IsEditor => Role is Role.Editor or Role.Administrator;

    public bool MayEdit => !Following && IsEditor;

    /// <summary>
    /// The Scratch Week is for the people who plan the timetable: editors and administrators see it,
    /// following or not (a viewer never does), and change it where <see cref="MayEdit"/> says.
    /// </summary>
    public bool MaySeeScratch => IsEditor;

    /// <summary>Anybody signed in can change something: a facilitator files their own registers.</summary>
    public bool MayEditSomething => !Following && SignedIn;

    public bool MayUseAdminTools => Role is Role.Administrator;

    public bool MayManageAccounts => Role is Role.Administrator;

    public bool MayManageProjects => Role is Role.Administrator;

    /// <summary>
    /// May look at, and prepare, a timetable that is not the one open for everybody, in a tab of their own
    /// (<see cref="ProjectChoice.ShownId"/>): next year's, or a file just brought in. An administrator's, as
    /// choosing which one is open is; everybody else only ever sees the open one.
    /// </summary>
    public bool MayLookAtOtherTimetables => Role is Role.Administrator;

    public bool SeesAdministratorsEvents => Role is Role.Administrator;

    /// <summary>
    /// Every register is theirs: to file, and to be counted as outstanding on. Not narrowed by
    /// <see cref="Following"/>, which the one door (<see cref="MayChange"/>) answers.
    /// </summary>
    public bool MayTakeEveryRegister => Role is Role.Administrator;

    public bool MaySeeSystem => Role is Role.Administrator;

    /// <summary>Pairs, renames and unpairs the screens in the building that show the timetable.</summary>
    public bool MayManageScreens => Role is Role.Administrator;

    /// <summary>The name changes are recorded under: the login, which is unique.</summary>
    public string By => Account?.Login ?? "";

    public string Display => Account?.Display ?? "";

    public Guid? FacilitatorId => Account?.FacilitatorId;

    public RegisterReader Registers => new(MayTakeEveryRegister, SignedIn ? FacilitatorId : null);

    public EventViewer Events => !SignedIn ? EventViewer.Nobody : new(
        SeesAdministratorsEvents, IsEditor,
        Account!.Tags.HasFlag(AccountTags.Student), FacilitatorId is not null,
        Account.Tags.HasFlag(AccountTags.Staff));

    private static readonly HashSet<string> EditorsCollections = ["sessions", "registers", "events", "scratch"];

    /// <summary>
    /// The server's own check of who may change which collection, whatever the screen offered: a
    /// viewer files registers only (and <c>Edits.FileRegister</c> narrows that to their own); an editor
    /// changes the week and its events; everything else is an administrator's; nobody signed in, nothing.
    /// </summary>
    public bool MayChange(string collection) =>
        !Following && (MayUseAdminTools
        || (MayEdit && EditorsCollections.Contains(collection))
        || (SignedIn && collection == "registers"));

    /// <summary>Who may print whose week: an administrator anybody's, everybody else their own.</summary>
    public bool MayHaveTheWeekOf(Guid facilitatorId) =>
        MayUseAdminTools || (SignedIn && FacilitatorId == facilitatorId);
}
