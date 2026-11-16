using CCCCScheduler.Models;

namespace CCCCScheduler.Helpers;

/// <summary>
/// Who a rename on <em>Manage ▸ Students…</em> would cost a surname, and what to tell them — in one
/// place, because both apps put the same question in front of the same people.
///
/// <para><b>The hazard.</b> The roster is a one-box list, so <c>ApplyStudentChanges</c> has to decide
/// what a whole typed string means for a name the model keeps in two fields. It puts the lot into
/// <see cref="Student.FirstName"/> and <b>blanks <see cref="Student.Surname"/></b> for any row whose
/// composed name changed — guessing nothing, splitting on nothing, which is the owner's own call and
/// is right. It is also gated on the name having actually changed, and that gate is load-bearing:
/// without it every OK would flatten every student in the project. But it means a rename here costs
/// a surname somebody typed in on purpose, and on Windows there is no profile screen to type it back
/// into. Silent, unwarned and unrecoverable is three too many.</para>
///
/// <para><b>Why the rule lives in Core rather than in each door.</b> Avalonia had this warning and
/// Windows did not, which is how the mitigation came to be "remember not to install 1.14.0 on
/// Windows" — a guard that depends on somebody's memory. Two hand-written copies of "when does it
/// fire" would be the same class of thing one layer down: this codebase has been bitten repeatedly by
/// the live path and the load path answering one question differently. One rule, two doors.</para>
///
/// <para><b>What each door still owns is the last sentence</b>, because the honest answer differs.
/// The Mac has <c>StudentProfileDialog</c> and can send the user there; Windows has nowhere to send
/// them, and saying "use Edit…" on a build with no Edit… would be worse than saying nothing.</para>
/// </summary>
public static class StudentRenameWarning
{
    /// <summary>What the Mac tells the user to do instead — it has a profile screen to send them to.</summary>
    public const string ProfileRemedy = "Use Edit… to set a first name and a surname separately.";

    /// <summary>
    /// What Windows tells them, which is the truth about Windows: there is no second door, so a
    /// surname cleared here cannot be typed back in through the app at all. Saying "use Edit…" on a
    /// build that has no Edit… would send somebody looking for a screen that is not there.
    /// </summary>
    public const string NoProfileRemedy =
        "This build has no other place to set a surname, so it cannot be put back afterwards.";

    /// <summary>
    /// The people <paramref name="rows"/> would cost a surname, named as they read <em>now</em>.
    ///
    /// <para><b>Two conditions, both required, and the second is the half that is easy to get wrong.</b>
    /// The row's name differs from what the student is called today — the <em>same</em> comparison
    /// <c>ApplyStudentChanges</c> gates on, so the warning and the effect cannot disagree — and that
    /// student has a surname to lose. Renaming somebody with no surname is silent, which is most of
    /// the roll in a project carried over from an older file. Warning always looks safer and is not: a
    /// prompt that fires when nothing is at stake gets clicked through, and then so does the one that
    /// matters.</para>
    ///
    /// <para>A row whose id names nobody is a student added on the list just now: there is nothing
    /// behind it yet and so nothing to lose.</para>
    /// </summary>
    public static IReadOnlyList<string> SurnamesLost(
        IEnumerable<Student> students, IEnumerable<(Guid Id, string Name)> rows)
    {
        // TryAdd rather than ToDictionary: a file carrying two students with one id would otherwise
        // throw on the UI thread, and this runs on the way into an apply that has its own reason for
        // tolerating exactly that.
        var byId = new Dictionary<Guid, Student>();
        foreach (var s in students) byId.TryAdd(s.Id, s);

        var losing = new List<string>();
        foreach (var (id, name) in rows)
        {
            if (!byId.TryGetValue(id, out var s)) continue;                        // added here: nothing to lose
            if (string.IsNullOrWhiteSpace(s.Surname)) continue;                    // nothing to lose either
            if (string.Equals(s.Name, name, StringComparison.Ordinal)) continue;   // untouched row
            losing.Add(s.Name);
        }
        return losing;
    }

    /// <summary>
    /// The question, worded once so a test can read what the app actually asks. It says what will
    /// happen, <b>who it will happen to</b>, and what to do about it — <paramref name="remedy"/>,
    /// which is the one sentence each platform answers for itself.
    ///
    /// <para>The people are named because "some surnames will be lost" is not something anybody can
    /// act on and "Robin van der Berg" is. All of them, if there are several: one visit can rename as
    /// many rows as it likes.</para>
    /// </summary>
    public static string Message(IReadOnlyList<string> names, string remedy)
        => "Renaming on this list replaces the whole name and clears the surname.\n\n"
           + "This will remove the surname for:\n"
           + string.Join("\n", names.Select(n => "    " + n))
           + "\n\n" + remedy + " Continue?";
}
