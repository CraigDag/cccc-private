using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Cccc.Store;

/// <summary>Three roles, plus nobody: the fail-closed default.</summary>
public enum Role { Nobody = 0, Viewer = 1, Editor = 2, Administrator = 3 }

[Flags]
public enum AccountTags { None = 0, Student = 1, Staff = 2 }

public sealed record Account(
    Guid Id, string Login, string Display, Role Role, AccountTags Tags, Guid? FacilitatorId,
    bool Suspended, bool MustChange, string Stamp, DateTimeOffset Created, bool HasPassphrase)
{
    /// <summary>What the account may do right now: a suspended account may do nothing.</summary>
    public Role Effective => Suspended ? Role.Nobody : Role;
}

/// <summary>
/// PBKDF2-SHA256 verifiers. Passphrases are never stored. The format is
/// <c>pbkdf2-sha256$iterations$salt$hash</c>, so the iteration count can rise later without a migration.
/// </summary>
public static class Verifier
{
    public const int Iterations = 600_000;
    public const int MinimumLength = 10;

    public static string Make(string passphrase, int iterations = Iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Check(string passphrase, string? verifier)
    {
        if (string.IsNullOrEmpty(verifier)) return false;
        var parts = verifier.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var it) || it < 1) return false;
        byte[] salt, want;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            want = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException) { return false; }
        var got = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, it, HashAlgorithmName.SHA256, want.Length);
        return CryptographicOperations.FixedTimeEquals(got, want);
    }

    /// <summary>Why a new passphrase is refused, or null.</summary>
    public static string? WhyNot(string? passphrase) =>
        string.IsNullOrEmpty(passphrase) || passphrase.Length < MinimumLength
            ? $"A passphrase needs at least {MinimumLength} characters. Several ordinary words make a good one."
            : null;
}

/// <summary>A change to accounts the guards refuse, in words for the administrator.</summary>
public sealed class AccountRefused(string message) : Exception(message);

/// <summary>
/// The accounts. Three guards, all checked under one lock against what is stored now: the person acting
/// must be an administrator now; the last administrator who can sign in cannot be removed, demoted or
/// suspended; nobody removes, demotes or suspends themself.
/// </summary>
public sealed class AccountDirectory(ProjectStore store, Func<DateTimeOffset> clock)
{
    private readonly Lock _gate = new();

    public IReadOnlyList<Account> All()
    {
        using var c = store.Connect();
        return Read(c, null).OrderBy(a => a.Display, Domain.NameOrder.Comparer).ToList();
    }

    public Account? Find(Guid id)
    {
        using var c = store.Connect();
        return Read(c, "id=$v", id.ToString()).FirstOrDefault();
    }

    public Account? FindByLogin(string login)
    {
        using var c = store.Connect();
        return Read(c, "login=$v", Normalise(login)).FirstOrDefault();
    }

    public bool Any() => All().Count > 0;

    /// <summary>Logins are compared as typed, trimmed, case-insensitively for ASCII only (SQLite NOCASE).</summary>
    public static string Normalise(string login) => login.Trim().Normalize(NormalizationForm.FormC);

    private static List<Account> Read(SqliteConnection c, string? where, string? value = null)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,login,display,role,tags,facilitator,suspended,must_change,stamp,created,verifier FROM accounts"
                          + (where is null ? "" : " WHERE " + where);
        if (value is not null) cmd.Parameters.AddWithValue("$v", value);
        var list = new List<Account>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Account(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2),
                Enum.TryParse<Role>(r.GetString(3), out var role) ? role : Role.Nobody,
                Enum.TryParse<AccountTags>(r.GetString(4), out var tags) ? tags : AccountTags.None,
                r.IsDBNull(5) ? null : Guid.Parse(r.GetString(5)), r.GetInt64(6) != 0, r.GetInt64(7) != 0,
                r.GetString(8), DateTimeOffset.Parse(r.GetString(9)), !r.IsDBNull(10)));
        return list;
    }

    /// <summary>Checks a sign-in. Returns the account only when the passphrase is right and it is not suspended.</summary>
    public Account? Verify(string login, string passphrase)
    {
        using var c = store.Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,verifier FROM accounts WHERE login=$l";
        cmd.Parameters.AddWithValue("$l", Normalise(login));
        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            Verifier.Check(passphrase, Dummy);     // the same work either way, so timing says nothing
            return null;
        }
        var id = Guid.Parse(r.GetString(0));
        var verifier = r.IsDBNull(1) ? null : r.GetString(1);
        if (!Verifier.Check(passphrase, verifier)) return null;
        var a = Find(id);
        return a is { Suspended: false } ? a : null;
    }

    private static readonly string Dummy = Verifier.Make("not a real passphrase at all", Verifier.Iterations);

    public void RecordSignIn(Guid id)
    {
        try
        {
            using var c = store.Connect();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO signins(account,at) VALUES($a,$t)";
            cmd.Parameters.AddWithValue("$a", id.ToString());
            cmd.Parameters.AddWithValue("$t", clock().ToString("O"));
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // A failed record of when somebody signed in must never stop them signing in.
        }
    }

    public IReadOnlyDictionary<Guid, DateTimeOffset> LastSignIns()
    {
        using var c = store.Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT account,at FROM signins";
        var d = new Dictionary<Guid, DateTimeOffset>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) d[Guid.Parse(r.GetString(0))] = DateTimeOffset.Parse(r.GetString(1));
        return d;
    }

    // ---- First start ------------------------------------------------------------------------------

    /// <summary>
    /// With no accounts at all, makes a one-time setup token for the first administrator. Returns null
    /// once any account exists, so the door closes itself.
    /// </summary>
    public string? SetupToken()
    {
        lock (_gate)
        {
            if (Any()) return null;
            using var c = store.Connect();
            using (var q = c.CreateCommand())
            {
                q.CommandText = "SELECT token FROM setup LIMIT 1";
                if (q.ExecuteScalar() is string existing) return existing;
            }
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO setup(token,created) VALUES($t,$c)";
            cmd.Parameters.AddWithValue("$t", token);
            cmd.Parameters.AddWithValue("$c", clock().ToString("O"));
            cmd.ExecuteNonQuery();
            return token;
        }
    }

    public Account CreateFirstAdministrator(string token, string login, string display, string passphrase)
    {
        lock (_gate)
        {
            if (Any()) throw new AccountRefused("The first administrator has already been made. Sign in instead.");
            using var c = store.Connect();
            using (var q = c.CreateCommand())
            {
                q.CommandText = "SELECT COUNT(*) FROM setup WHERE token=$t";
                q.Parameters.AddWithValue("$t", token);
                if ((long)q.ExecuteScalar()! == 0) throw new AccountRefused("That setup link is not this store's.");
            }
            if (Verifier.WhyNot(passphrase) is { } why) throw new AccountRefused(why);
            var a = Insert(c, login, display, Role.Administrator, Verifier.Make(passphrase), mustChange: false);
            using var del = c.CreateCommand();
            del.CommandText = "DELETE FROM setup";
            del.ExecuteNonQuery();
            return a;
        }
    }

    private Account Insert(SqliteConnection c, string login, string display, Role role, string? verifier, bool mustChange)
    {
        login = Normalise(login);
        if (login.Length == 0) throw new AccountRefused("An account needs a sign-in name.");
        if (Read(c, "login=$v", login).Count > 0) throw new AccountRefused("There is already an account with that sign-in name.");
        var id = Guid.NewGuid();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO accounts(id,login,display,verifier,role,tags,facilitator,suspended,must_change,stamp,created)
            VALUES($id,$l,$d,$v,$r,'None',NULL,0,$m,$s,$c)
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$l", login);
        cmd.Parameters.AddWithValue("$d", string.IsNullOrWhiteSpace(display) ? login : display.Trim());
        cmd.Parameters.AddWithValue("$v", (object?)verifier ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$r", role.ToString());
        cmd.Parameters.AddWithValue("$m", mustChange ? 1 : 0);
        cmd.Parameters.AddWithValue("$s", NewStamp());
        cmd.Parameters.AddWithValue("$c", clock().ToString("O"));
        cmd.ExecuteNonQuery();
        return Read(c, "id=$v", id.ToString()).Single();
    }

    private static string NewStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(12));

    // ---- An administrator's changes ---------------------------------------------------------------

    private void MustBeAdministrator(SqliteConnection c, Guid actingAs)
    {
        var me = Read(c, "id=$v", actingAs.ToString()).FirstOrDefault();
        if (me?.Effective != Role.Administrator) throw new AccountRefused("Only an administrator can change accounts.");
    }

    private void KeepAnAdministrator(SqliteConnection c, Guid target, Guid actingAs, Role newRole, bool suspended, bool removing)
    {
        var all = Read(c, null);
        var t = all.FirstOrDefault(a => a.Id == target) ?? throw new AccountRefused("That account has gone.");
        bool losesAdmin = t.Effective == Role.Administrator && (removing || suspended || newRole != Role.Administrator);
        if (losesAdmin && all.Count(a => a.Effective == Role.Administrator) <= 1)
            throw new AccountRefused("This is the last administrator who can sign in. Make somebody else an administrator first.");
        if (target == actingAs && (removing || suspended || newRole != t.Role))
            throw new AccountRefused("You cannot remove, demote or suspend yourself. Ask another administrator.");
    }

    /// <summary>Adds an account with a passphrase the administrator set; the person must choose their own on first sign-in.</summary>
    public Account Add(Guid actingAs, string login, string display, Role role, string passphrase)
    {
        lock (_gate)
        {
            using var c = store.Connect();
            MustBeAdministrator(c, actingAs);
            if (Verifier.WhyNot(passphrase) is { } why) throw new AccountRefused(why);
            return Insert(c, login, display, role, Verifier.Make(passphrase), mustChange: true);
        }
    }

    public void Update(Guid actingAs, Guid target, string display, Role role, AccountTags tags, Guid? facilitatorId, bool suspended)
    {
        lock (_gate)
        {
            using var c = store.Connect();
            MustBeAdministrator(c, actingAs);
            KeepAnAdministrator(c, target, actingAs, role, suspended, removing: false);
            var before = Read(c, "id=$v", target.ToString()).Single();
            bool stampMoves = before.Role != role || before.Suspended != suspended;
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE accounts SET display=$d, role=$r, tags=$t, facilitator=$f, suspended=$s" + (stampMoves ? ", stamp=$st" : "") + " WHERE id=$id";
            cmd.Parameters.AddWithValue("$d", string.IsNullOrWhiteSpace(display) ? before.Login : display.Trim());
            cmd.Parameters.AddWithValue("$r", role.ToString());
            cmd.Parameters.AddWithValue("$t", tags.ToString());
            cmd.Parameters.AddWithValue("$f", (object?)facilitatorId?.ToString() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$s", suspended ? 1 : 0);
            if (stampMoves) cmd.Parameters.AddWithValue("$st", NewStamp());
            cmd.Parameters.AddWithValue("$id", target.ToString());
            cmd.ExecuteNonQuery();
        }
    }

    public void Reset(Guid actingAs, Guid target, string passphrase)
    {
        lock (_gate)
        {
            using var c = store.Connect();
            MustBeAdministrator(c, actingAs);
            if (Verifier.WhyNot(passphrase) is { } why) throw new AccountRefused(why);
            SetVerifier(c, target, Verifier.Make(passphrase), mustChange: true);
        }
    }

    public void Remove(Guid actingAs, Guid target)
    {
        lock (_gate)
        {
            using var c = store.Connect();
            MustBeAdministrator(c, actingAs);
            KeepAnAdministrator(c, target, actingAs, Role.Nobody, true, removing: true);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM accounts WHERE id=$id; DELETE FROM signins WHERE account=$id;";
            cmd.Parameters.AddWithValue("$id", target.ToString());
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Everybody's own. Moves the stamp, which signs the person out everywhere else.</summary>
    public string ChangeOwn(Guid id, string current, string next)
    {
        lock (_gate)
        {
            var a = Find(id) ?? throw new AccountRefused("That account has gone.");
            if (Verify(a.Login, current) is null) throw new AccountRefused("That is not your current passphrase.");
            if (Verifier.WhyNot(next) is { } why) throw new AccountRefused(why);
            if (current == next) throw new AccountRefused("Choose a passphrase different from the one you have.");
            using var c = store.Connect();
            return SetVerifier(c, id, Verifier.Make(next), mustChange: false);
        }
    }

    private static string SetVerifier(SqliteConnection c, Guid id, string verifier, bool mustChange)
    {
        var stamp = NewStamp();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE accounts SET verifier=$v, must_change=$m, stamp=$s WHERE id=$id";
        cmd.Parameters.AddWithValue("$v", verifier);
        cmd.Parameters.AddWithValue("$m", mustChange ? 1 : 0);
        cmd.Parameters.AddWithValue("$s", stamp);
        cmd.Parameters.AddWithValue("$id", id.ToString());
        if (cmd.ExecuteNonQuery() == 0) throw new AccountRefused("That account has gone.");
        return stamp;
    }

    /// <summary>For made-up preview accounts only: they start ready to use.</summary>
    public void ClearMustChange(Guid id)
    {
        using var c = store.Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE accounts SET must_change=0 WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }
}
