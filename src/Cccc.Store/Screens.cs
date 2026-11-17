using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Cccc.Store;

/// <summary>
/// A screen in the building paired to show the timetable as it is now: the whole building, or one
/// space's day when <see cref="RoomId"/> is set (a tablet by its door).
/// </summary>
public sealed record Screen(Guid Id, string Name, Guid? RoomId, DateTimeOffset Paired, DateTimeOffset? Seen);

/// <summary>A change to screens refused, in words for the administrator.</summary>
public sealed class ScreenRefused(string message) : Exception(message);

/// <summary>
/// The screens paired to show the building. A screen holds a long random token in a cookie, and the
/// store keeps only its hash, so a copy of the store (a backup) does not let anybody pose as one.
/// Unpairing deletes the row, and the screen asks to be paired again the next time it looks.
/// Nothing here is a person's: a screen has a name like "Corridor", and who paired it is not kept.
/// </summary>
public sealed class ScreenDirectory(ProjectStore store)
{
    public const int MaxScreens = 50;
    public const int MaxName = 60;

    /// <summary>How stale a screen's "last seen" may get before a look writes it again.</summary>
    public static readonly TimeSpan SeenEvery = TimeSpan.FromMinutes(5);

    public (Screen Screen, string Token) Pair(string name, Guid? roomId, DateTimeOffset now)
    {
        var clean = Clean(name);
        using var c = store.Connect();
        using var count = c.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM screens";
        if ((long)count.ExecuteScalar()! >= MaxScreens)
            throw new ScreenRefused($"There are already {MaxScreens} screens. Unpair one that is no longer used first.");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var screen = new Screen(Guid.NewGuid(), clean, roomId, now, null);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO screens(id,name,room,token,paired,seen) VALUES($i,$n,$r,$t,$p,NULL)";
        cmd.Parameters.AddWithValue("$i", screen.Id.ToString());
        cmd.Parameters.AddWithValue("$n", clean);
        cmd.Parameters.AddWithValue("$r", (object?)roomId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$t", Hash(token));
        cmd.Parameters.AddWithValue("$p", now.ToString("O", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
        return (screen, token);
    }

    /// <summary>The screen a token belongs to, or null: never paired, unpaired since, or not a token at all.</summary>
    public Screen? FindByToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) return null;
        using var c = store.Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,name,room,paired,seen FROM screens WHERE token=$t";
        cmd.Parameters.AddWithValue("$t", Hash(token));
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    public IReadOnlyList<Screen> All()
    {
        using var c = store.Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,name,room,paired,seen FROM screens";
        using var r = cmd.ExecuteReader();
        var all = new List<Screen>();
        while (r.Read()) all.Add(Read(r));
        return all.OrderBy(s => s.Name, Domain.NameOrder.Comparer).ToList();
    }

    public Screen? Find(Guid id) => All().FirstOrDefault(s => s.Id == id);

    /// <summary>A new name, or a different thing to show. The token stays, so the screen carries on.</summary>
    public void Change(Guid id, string name, Guid? roomId)
    {
        var clean = Clean(name);
        using var c = store.Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE screens SET name=$n, room=$r WHERE id=$i";
        cmd.Parameters.AddWithValue("$i", id.ToString());
        cmd.Parameters.AddWithValue("$n", clean);
        cmd.Parameters.AddWithValue("$r", (object?)roomId?.ToString() ?? DBNull.Value);
        if (cmd.ExecuteNonQuery() == 0) throw new ScreenRefused("That screen is no longer paired.");
    }

    /// <summary>Forgets a screen: its token stops working at once.</summary>
    public bool Unpair(Guid id)
    {
        using var c = store.Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM screens WHERE id=$i";
        cmd.Parameters.AddWithValue("$i", id.ToString());
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// Notes that a screen looked. Written at most every <see cref="SeenEvery"/>, so a screen refreshing
    /// every half minute all day does not write to the store every half minute.
    /// </summary>
    public void Saw(Screen screen, DateTimeOffset now)
    {
        if (screen.Seen is { } seen && now - seen < SeenEvery) return;
        using var c = store.Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE screens SET seen=$s WHERE id=$i";
        cmd.Parameters.AddWithValue("$i", screen.Id.ToString());
        cmd.Parameters.AddWithValue("$s", now.ToString("O", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    internal static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Clean(string name)
    {
        var clean = new string((name ?? "").Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (clean.Length == 0) throw new ScreenRefused("Give the screen a name, such as where it hangs.");
        if (clean.Length > MaxName) throw new ScreenRefused($"A screen's name can be at most {MaxName} characters.");
        return clean;
    }

    private static Screen Read(Microsoft.Data.Sqlite.SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), r.GetString(1),
        r.IsDBNull(2) ? null : Guid.Parse(r.GetString(2)),
        DateTimeOffset.Parse(r.GetString(3), CultureInfo.InvariantCulture),
        r.IsDBNull(4) ? null : DateTimeOffset.Parse(r.GetString(4), CultureInfo.InvariantCulture));
}
