using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Microsoft.Data.Sqlite;

namespace Cccc.Store;

public sealed record ProjectInfo(Guid Id, string Name, DateTimeOffset Created, DateTimeOffset LastChanged, string LastChangedBy, DateTimeOffset? Deleted);

/// <summary>A batch as listed in a project's history.</summary>
public sealed record HistoryEntry(long Sequence, string By, DateTimeOffset At, string Label, int Changes);

/// <summary>Why the store refused to start, in a sentence for whoever reads the log.</summary>
public sealed class StoreRefused(string message) : Exception(message);

/// <summary>
/// One SQLite file holding every project's operation log, its snapshots, and the accounts. Every write
/// is a SQLite transaction (WAL), so a crash mid-write leaves the last whole state. No project name
/// and no project content is ever logged by this class.
/// </summary>
public sealed class ProjectStore
{
    public const int FormatVersion = 1;
    public const int SnapshotEvery = 200;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    private readonly string _connectionString;

    public string DatabasePath { get; }

    private ProjectStore(string path)
    {
        DatabasePath = path;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
        }.ToString();
    }

    /// <summary>
    /// Opens the store in <paramref name="folder"/>. Refuses a folder that is missing (unless
    /// <paramref name="create"/> — so a vanished volume is never silently replaced by an empty one),
    /// inside the temporary directory, or not writable. There is no "accept it anyway".
    /// </summary>
    public static ProjectStore Open(string folder, bool create = false)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new StoreRefused("No store folder is set. Set Store__Path to a folder on a volume that survives a restart.");
        var full = Path.GetFullPath(folder);
        var temp = Path.GetFullPath(Path.GetTempPath());
        if (full.StartsWith(temp, StringComparison.Ordinal) && !AllowTemp)
            throw new StoreRefused($"The store folder {full} is inside the temporary directory, which is emptied on a restart.");
        if (!Directory.Exists(full))
        {
            if (!create)
                throw new StoreRefused($"The store folder {full} does not exist. If this is the first start, start once with Store__Create=true. If it is not, the volume holding it has gone: do not create it, find it.");
            Directory.CreateDirectory(full);
        }
        var probe = Path.Combine(full, ".write-probe");
        try
        {
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new StoreRefused($"The store folder {full} cannot be written to.");
        }
        var path = Path.Combine(full, "cccc.db");
        bool exists = File.Exists(path);
        if (!exists && !create && Directory.EnumerateFileSystemEntries(full).Any())
            throw new StoreRefused($"The store folder {full} has files in it but no cccc.db. Refusing to start a new store over them.");
        var create_cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
        using (var c = new SqliteConnection(create_cs))
        {
            c.Open();
            Exec(c, "PRAGMA journal_mode=WAL;");
            Exec(c, Schema);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT value FROM meta WHERE key='format'";
            var v = cmd.ExecuteScalar() as string;
            if (v is null) Exec(c, $"INSERT INTO meta(key,value) VALUES('format','{FormatVersion}')");
            else if (int.Parse(v) > FormatVersion)
                throw new StoreRefused($"This store was written by a newer version of the app (store format {v}; this version understands {FormatVersion}). Update the app to open it.");
        }
        return new ProjectStore(path);
    }

    /// <summary>For tests only: lets a store live in the temporary directory.</summary>
    internal static bool AllowTemp { get; set; }

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS projects (
            id TEXT PRIMARY KEY, name TEXT NOT NULL, created TEXT NOT NULL,
            last_changed TEXT NOT NULL, last_changed_by TEXT NOT NULL, deleted TEXT NULL);
        CREATE TABLE IF NOT EXISTS batches (
            project TEXT NOT NULL, seq INTEGER NOT NULL, by TEXT NOT NULL, at TEXT NOT NULL,
            label TEXT NOT NULL, ops BLOB NOT NULL, PRIMARY KEY (project, seq));
        CREATE TABLE IF NOT EXISTS snapshots (
            project TEXT NOT NULL, seq INTEGER NOT NULL, at TEXT NOT NULL, state BLOB NOT NULL,
            PRIMARY KEY (project, seq));
        CREATE TABLE IF NOT EXISTS accounts (
            id TEXT PRIMARY KEY, login TEXT NOT NULL UNIQUE COLLATE NOCASE, display TEXT NOT NULL,
            verifier TEXT NULL, role TEXT NOT NULL, tags TEXT NOT NULL, facilitator TEXT NULL,
            suspended INTEGER NOT NULL DEFAULT 0, must_change INTEGER NOT NULL DEFAULT 0,
            stamp TEXT NOT NULL, created TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS signins (account TEXT PRIMARY KEY, at TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS setup (token TEXT PRIMARY KEY, created TEXT NOT NULL);
        """;

    internal SqliteConnection Connect()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        Exec(c, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
        return c;
    }

    private static void Exec(SqliteConnection c, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string Iso(DateTimeOffset t) => t.ToUniversalTime().ToString("O");

    private static DateTimeOffset FromIso(string s) => DateTimeOffset.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind);

    // ---- Projects ---------------------------------------------------------------------------------

    public IReadOnlyList<ProjectInfo> List(bool includeDeleted = false)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,name,created,last_changed,last_changed_by,deleted FROM projects" + (includeDeleted ? "" : " WHERE deleted IS NULL");
        var list = new List<ProjectInfo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ProjectInfo(Guid.Parse(r.GetString(0)), r.GetString(1), FromIso(r.GetString(2)), FromIso(r.GetString(3)),
                r.GetString(4), r.IsDBNull(5) ? null : FromIso(r.GetString(5))));
        return list.OrderBy(p => p.Name, Domain.NameOrder.Comparer).ToList();
    }

    public ProjectInfo? Find(Guid id) => List(includeDeleted: true).FirstOrDefault(p => p.Id == id);

    /// <summary>The project most recently CHANGED — where signing in lands. Reading never moves it.</summary>
    public ProjectInfo? Landing() =>
        List().OrderByDescending(p => p.LastChanged).ThenBy(p => p.Name, Domain.NameOrder.Comparer).FirstOrDefault();

    public Guid Create(string name, Timetable initial, string by, DateTimeOffset at)
    {
        var id = Guid.NewGuid();
        using var c = Connect();
        using var tx = c.BeginTransaction();
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO projects(id,name,created,last_changed,last_changed_by) VALUES($id,$n,$t,$t,$b)";
            cmd.Parameters.AddWithValue("$id", id.ToString());
            cmd.Parameters.AddWithValue("$n", name.Trim());
            cmd.Parameters.AddWithValue("$t", Iso(at));
            cmd.Parameters.AddWithValue("$b", by);
            cmd.ExecuteNonQuery();
        }
        WriteSnapshot(c, tx, id, 0, initial with { Settings = initial.Settings with { Name = name.Trim() } }, at);
        tx.Commit();
        return id;
    }

    public void Rename(Guid id, string name)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE projects SET name=$n WHERE id=$id";
        cmd.Parameters.AddWithValue("$n", name.Trim());
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }

    /// <summary>A soft delete: the project is hidden and comes back with <see cref="Undelete"/> for 30 days.</summary>
    public void Delete(Guid id, DateTimeOffset at) => SetDeleted(id, Iso(at));

    public void Undelete(Guid id) => SetDeleted(id, null);

    private void SetDeleted(Guid id, string? at)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE projects SET deleted=$d WHERE id=$id";
        cmd.Parameters.AddWithValue("$d", (object?)at ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.ExecuteNonQuery();
    }

    // ---- The log ----------------------------------------------------------------------------------

    /// <summary>The current state and its sequence: the newest snapshot, then every batch after it.</summary>
    public (Timetable State, long Sequence) Load(Guid id) => LoadAt(id, long.MaxValue);

    /// <summary>The state as it stood after batch <paramref name="upTo"/>.</summary>
    public (Timetable State, long Sequence) LoadAt(Guid id, long upTo)
    {
        using var c = Connect();
        long snapSeq;
        Timetable state;
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT seq,state FROM snapshots WHERE project=$p AND seq<=$u ORDER BY seq DESC LIMIT 1";
            cmd.Parameters.AddWithValue("$p", id.ToString());
            cmd.Parameters.AddWithValue("$u", upTo);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new InvalidOperationException("That project has no stored state from that far back.");
            snapSeq = r.GetInt64(0);
            state = ModelJson.Deserialise(Unzip((byte[])r[1]));
        }
        long seq = snapSeq;
        foreach (var change in ReadChanges(c, id, snapSeq, upTo))
        {
            state = Apply.Batch(state, change.Batch);
            seq = change.Sequence;
        }
        return (state, seq);
    }

    private static IEnumerable<Change> ReadChanges(SqliteConnection c, Guid id, long after, long upTo)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT seq,by,at,label,ops FROM batches WHERE project=$p AND seq>$a AND seq<=$u ORDER BY seq";
        cmd.Parameters.AddWithValue("$p", id.ToString());
        cmd.Parameters.AddWithValue("$a", after);
        cmd.Parameters.AddWithValue("$u", upTo);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var ops = JsonSerializer.Deserialize<List<Op>>(Unzip((byte[])r[4]), ModelJson.Options) ?? [];
            yield return new Change(r.GetInt64(0), r.GetString(1), FromIso(r.GetString(2)), new Batch(r.GetString(3), [.. ops]));
        }
    }

    /// <summary>
    /// Appends one batch as sequence <paramref name="sequence"/>, in one transaction with the project's
    /// last-changed stamp. A snapshot is added every <see cref="SnapshotEvery"/> batches.
    /// </summary>
    public void Append(Guid id, Change change, Func<Timetable>? stateAfter = null)
    {
        using var c = Connect();
        using var tx = c.BeginTransaction();
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO batches(project,seq,by,at,label,ops) VALUES($p,$s,$b,$t,$l,$o)";
            cmd.Parameters.AddWithValue("$p", id.ToString());
            cmd.Parameters.AddWithValue("$s", change.Sequence);
            cmd.Parameters.AddWithValue("$b", change.By);
            cmd.Parameters.AddWithValue("$t", Iso(change.At));
            cmd.Parameters.AddWithValue("$l", change.Batch.Label);
            cmd.Parameters.AddWithValue("$o", Zip(JsonSerializer.Serialize(change.Batch.Ops, ModelJson.Options)));
            cmd.ExecuteNonQuery();
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE projects SET last_changed=$t, last_changed_by=$b WHERE id=$p";
            cmd.Parameters.AddWithValue("$p", id.ToString());
            cmd.Parameters.AddWithValue("$t", Iso(change.At));
            cmd.Parameters.AddWithValue("$b", change.By);
            cmd.ExecuteNonQuery();
        }
        if (stateAfter is not null && change.Sequence % SnapshotEvery == 0)
            WriteSnapshot(c, tx, id, change.Sequence, stateAfter(), change.At);
        tx.Commit();
    }

    public void Snapshot(Guid id, long sequence, Timetable state, DateTimeOffset at)
    {
        using var c = Connect();
        using var tx = c.BeginTransaction();
        WriteSnapshot(c, tx, id, sequence, state, at);
        tx.Commit();
    }

    private static void WriteSnapshot(SqliteConnection c, SqliteTransaction tx, Guid id, long seq, Timetable state, DateTimeOffset at)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR REPLACE INTO snapshots(project,seq,at,state) VALUES($p,$s,$t,$st)";
        cmd.Parameters.AddWithValue("$p", id.ToString());
        cmd.Parameters.AddWithValue("$s", seq);
        cmd.Parameters.AddWithValue("$t", Iso(at));
        cmd.Parameters.AddWithValue("$st", Zip(ModelJson.Serialise(state)));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<HistoryEntry> History(Guid id, int limit = 200)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT seq,by,at,label,ops FROM batches WHERE project=$p ORDER BY seq DESC LIMIT $l";
        cmd.Parameters.AddWithValue("$p", id.ToString());
        cmd.Parameters.AddWithValue("$l", limit);
        var list = new List<HistoryEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            int n = JsonNode.Parse(Unzip((byte[])r[4]))?.AsArray().Count ?? 0;
            list.Add(new HistoryEntry(r.GetInt64(0), r.GetString(1), FromIso(r.GetString(2)), r.GetString(3), n));
        }
        return list;
    }

    public IReadOnlyList<Change> ChangesSince(Guid id, long after)
    {
        using var c = Connect();
        return ReadChanges(c, id, after, long.MaxValue).ToList();
    }

    /// <summary>
    /// Folds history older than <see cref="Retention"/> into one snapshot, and removes projects deleted
    /// longer ago than that for good. Run from every listing and at start-up.
    /// </summary>
    public void Prune(DateTimeOffset now)
    {
        var cutoff = now - Retention;
        foreach (var p in List(includeDeleted: true))
        {
            if (p.Deleted is { } d && d < cutoff) { Purge(p.Id); continue; }
            long? lastOld;
            using (var c = Connect())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT MAX(seq) FROM batches WHERE project=$p AND at<$c";
                cmd.Parameters.AddWithValue("$p", p.Id.ToString());
                cmd.Parameters.AddWithValue("$c", Iso(cutoff));
                lastOld = cmd.ExecuteScalar() is long l ? l : null;
            }
            if (lastOld is not { } upTo) continue;
            var (state, seq) = LoadAt(p.Id, upTo);
            using var c2 = Connect();
            using var tx = c2.BeginTransaction();
            WriteSnapshot(c2, tx, p.Id, seq, state, now);
            Exec(c2, $"DELETE FROM batches WHERE project='{p.Id}' AND seq<={seq}", tx);
            Exec(c2, $"DELETE FROM snapshots WHERE project='{p.Id}' AND seq<{seq}", tx);
            tx.Commit();
        }
    }

    private void Purge(Guid id)
    {
        using var c = Connect();
        using var tx = c.BeginTransaction();
        foreach (var t in new[] { "batches", "snapshots" }) Exec(c, $"DELETE FROM {t} WHERE project='{id}'", tx);
        Exec(c, $"DELETE FROM projects WHERE id='{id}'", tx);
        tx.Commit();
    }

    /// <summary>
    /// Rewrites every stored batch and snapshot of a project through <paramref name="scrub"/> — the one
    /// sanctioned rewrite of history, for erasing a person. Returns how many stored values changed.
    /// </summary>
    public int Rewrite(Guid id, Func<string, string> scrub)
    {
        using var c = Connect();
        using var tx = c.BeginTransaction();
        int changed = 0;
        foreach (var table in new[] { ("batches", "ops"), ("snapshots", "state") })
        {
            var rows = new List<(long Seq, byte[] Blob)>();
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"SELECT seq,{table.Item2} FROM {table.Item1} WHERE project=$p";
                cmd.Parameters.AddWithValue("$p", id.ToString());
                using var r = cmd.ExecuteReader();
                while (r.Read()) rows.Add((r.GetInt64(0), (byte[])r[1]));
            }
            foreach (var (seq, blob) in rows)
            {
                var before = Unzip(blob);
                var after = scrub(before);
                if (after == before) continue;
                using var up = c.CreateCommand();
                up.Transaction = tx;
                up.CommandText = $"UPDATE {table.Item1} SET {table.Item2}=$v WHERE project=$p AND seq=$s";
                up.Parameters.AddWithValue("$v", Zip(after));
                up.Parameters.AddWithValue("$p", id.ToString());
                up.Parameters.AddWithValue("$s", seq);
                up.ExecuteNonQuery();
                changed++;
            }
        }
        tx.Commit();
        Exec(Connect(), "VACUUM;");
        return changed;
    }

    /// <summary>A consistent copy of the whole store while the app runs, for the nightly off-site backup.</summary>
    public void Backup(string path)
    {
        if (File.Exists(path)) throw new IOException("The backup file already exists.");
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "VACUUM INTO $p";
        cmd.Parameters.AddWithValue("$p", Path.GetFullPath(path));
        cmd.ExecuteNonQuery();
    }

    private static byte[] Zip(string s)
    {
        using var ms = new MemoryStream();
        using (var z = new BrotliStream(ms, CompressionLevel.Fastest)) z.Write(Encoding.UTF8.GetBytes(s));
        return ms.ToArray();
    }

    private static string Unzip(byte[] b)
    {
        using var z = new BrotliStream(new MemoryStream(b), CompressionMode.Decompress);
        using var r = new StreamReader(z, Encoding.UTF8);
        return r.ReadToEnd();
    }
}
