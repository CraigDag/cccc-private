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
    /// <summary>
    /// The store's format. 2 added the Scratch Week's collection: a build of format 1 would replay one of
    /// its changes as a collection it has never heard of, or drop the plan at its next snapshot, so it
    /// refuses a store of format 2 instead. 3 added former classes, spaces and facilitators
    /// (<c>FormerSince</c>): a build of format 2 would read every former one as in use and offer it ahead
    /// again. 4 added what facilitators ask (asking to be away, offering to cover): a build of format 3 would
    /// replay one as a collection it has never heard of. 5 added equipment (what a space has, what a class
    /// needs): a build of format 4 would replay a piece as a collection it has never heard of, and save a space
    /// or a class back without what it has or needs. 6 added asking for a space: a build of format 5 would
    /// replay one as a collection it has never heard of. 7 added equipment out of use, the same. 8 added how many
    /// students one facilitator may have in a class: a build of format 7 would save a class back without it. An older
    /// store has none of these and opens as it is.
    /// </summary>
    public const int FormatVersion = 8;
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
        // An EMPTY folder is refused too: Docker creates a missing bind-mount folder empty, so a volume
        // that went away arrives looking exactly like a first start. Only Store__Create starts a store.
        if (!exists && !create)
            throw new StoreRefused(Directory.EnumerateFileSystemEntries(full).Any()
                ? $"The store folder {full} has files in it but no cccc.db. Refusing to start a new store over them."
                : $"The store folder {full} is empty. If this is the first start, start once with Store__Create=true. If it is not, the volume holding the store has gone: do not create it, find it.");
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
            else if (int.Parse(v) < FormatVersion)
                Exec(c, $"UPDATE meta SET value='{FormatVersion}' WHERE key='format'");
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
        CREATE TABLE IF NOT EXISTS calendar_files (
            project TEXT NOT NULL, person TEXT NOT NULL, held TEXT NOT NULL, PRIMARY KEY (project, person));
        CREATE TABLE IF NOT EXISTS news_seen (
            project TEXT NOT NULL, person TEXT NOT NULL, seen TEXT NOT NULL, PRIMARY KEY (project, person));
        CREATE TABLE IF NOT EXISTS screens (
            id TEXT PRIMARY KEY, name TEXT NOT NULL, room TEXT, token TEXT NOT NULL UNIQUE,
            paired TEXT NOT NULL, seen TEXT);
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

    // ---- Settings of the store itself -------------------------------------------------------------

    /// <summary>A store-wide value: which project is open, when the last backup ran. Never project contents.</summary>
    public string? GetMeta(string key)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key=$k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetMeta(string key, string? value)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = value is null ? "DELETE FROM meta WHERE key=$k"
            : "INSERT INTO meta(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v";
        cmd.Parameters.AddWithValue("$k", key);
        if (value is not null) cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    // ---- What a person's calendar file held -------------------------------------------------------

    /// <summary>
    /// What the downloads of one person's phone calendar have held, as the web's own text: ids and times,
    /// never a title, so the next download can cancel what has gone since. It goes when its project goes.
    /// </summary>
    public string? CalendarHeld(Guid project, Guid person)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT held FROM calendar_files WHERE project=$p AND person=$f";
        cmd.Parameters.AddWithValue("$p", project.ToString());
        cmd.Parameters.AddWithValue("$f", person.ToString());
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Forgets what every timetable's downloads of one person's calendar carried: they were erased.</summary>
    public void ForgetCalendars(Guid person)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM calendar_files WHERE person=$f";
        cmd.Parameters.AddWithValue("$f", person.ToString());
        cmd.ExecuteNonQuery();
    }

    public void SetCalendarHeld(Guid project, Guid person, string held)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO calendar_files(project,person,held) VALUES($p,$f,$h) ON CONFLICT(project,person) DO UPDATE SET held=$h";
        cmd.Parameters.AddWithValue("$p", project.ToString());
        cmd.Parameters.AddWithValue("$f", person.ToString());
        cmd.Parameters.AddWithValue("$h", held);
        cmd.ExecuteNonQuery();
    }

    // ---- What a facilitator has seen of what changed for them -------------------------------------

    /// <summary>
    /// The last change to each of one facilitator's classes that they have said they saw ("Got it" on Now), as the
    /// web's own text: class ids and places in the history, nothing else. It goes when its project goes.
    /// </summary>
    public string? NewsSeen(Guid project, Guid person)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT seen FROM news_seen WHERE project=$p AND person=$f";
        cmd.Parameters.AddWithValue("$p", project.ToString());
        cmd.Parameters.AddWithValue("$f", person.ToString());
        return cmd.ExecuteScalar() as string;
    }

    public void SetNewsSeen(Guid project, Guid person, string seen)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO news_seen(project,person,seen) VALUES($p,$f,$s) ON CONFLICT(project,person) DO UPDATE SET seen=$s";
        cmd.Parameters.AddWithValue("$p", project.ToString());
        cmd.Parameters.AddWithValue("$f", person.ToString());
        cmd.Parameters.AddWithValue("$s", seen);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Forgets what one facilitator has seen, in every timetable: they were erased.</summary>
    public void ForgetNewsSeen(Guid person)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM news_seen WHERE person=$f";
        cmd.Parameters.AddWithValue("$f", person.ToString());
        cmd.ExecuteNonQuery();
    }

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
        foreach (var change in ReadRows(cmd)) yield return change;
    }

    /// <summary>
    /// Every change made at or after <paramref name="since"/>, in order. Times are stored as UTC in one
    /// fixed-width form, so comparing them as text is comparing them as times.
    /// </summary>
    public IReadOnlyList<Change> ChangesMadeSince(Guid id, DateTimeOffset since)
    {
        using var c = Connect();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT seq,by,at,label,ops FROM batches WHERE project=$p AND at>=$t ORDER BY seq";
        cmd.Parameters.AddWithValue("$p", id.ToString());
        cmd.Parameters.AddWithValue("$t", Iso(since));
        return ReadRows(cmd).ToList();
    }

    private static IEnumerable<Change> ReadRows(SqliteCommand cmd)
    {
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
        foreach (var t in new[] { "batches", "snapshots", "calendar_files", "news_seen" }) Exec(c, $"DELETE FROM {t} WHERE project='{id}'", tx);
        Exec(c, $"DELETE FROM projects WHERE id='{id}'", tx);
        tx.Commit();
    }

    /// <summary>
    /// Rewrites every stored batch and snapshot of a project through <paramref name="scrub"/>, and every
    /// batch's label through <paramref name="relabel"/> — the one sanctioned rewrite of history, for erasing
    /// a person. A label is words rather than a record with an id, so it is reached by the person's names.
    /// Returns how many stored values changed.
    /// </summary>
    public int Rewrite(Guid id, Func<string, string> scrub, Func<string, string>? relabel = null)
    {
        using var c = Connect();
        using var tx = c.BeginTransaction();
        int changed = 0;
        foreach (var table in new[] { ("batches", "ops"), ("snapshots", "state") })
        {
            bool labelled = table.Item1 == "batches";
            var rows = new List<(long Seq, byte[] Blob, string? Label)>();
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"SELECT seq,{table.Item2}{(labelled ? ",label" : "")} FROM {table.Item1} WHERE project=$p";
                cmd.Parameters.AddWithValue("$p", id.ToString());
                using var r = cmd.ExecuteReader();
                while (r.Read()) rows.Add((r.GetInt64(0), (byte[])r[1], labelled ? r.GetString(2) : null));
            }
            foreach (var (seq, blob, label) in rows)
            {
                var before = Unzip(blob);
                var after = scrub(before);
                var newLabel = label is not null && relabel is not null ? relabel(label) : label;
                if (after == before && newLabel == label) continue;
                using var up = c.CreateCommand();
                up.Transaction = tx;
                up.CommandText = $"UPDATE {table.Item1} SET {table.Item2}=$v{(labelled ? ",label=$l" : "")} WHERE project=$p AND seq=$s";
                up.Parameters.AddWithValue("$v", Zip(after));
                if (labelled) up.Parameters.AddWithValue("$l", newLabel);
                up.Parameters.AddWithValue("$p", id.ToString());
                up.Parameters.AddWithValue("$s", seq);
                up.ExecuteNonQuery();
                changed++;
            }
        }
        tx.Commit();
        using (var v = Connect())
        {
            Exec(v, "VACUUM;");
            // The store keeps a write-ahead log: until a checkpoint, the vacuumed pages are only in the -wal file,
            // and the database file and the log both still hold what was rewritten. Move them into the database
            // and empty the log, so what was erased is in neither file.
            Exec(v, "PRAGMA wal_checkpoint(TRUNCATE);");
        }
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
        // Administration ▸ System reads this, so a backup that stopped running is visible on screen
        // rather than only to whoever reads the laptop's journal.
        SetMeta("last_backup", DateTimeOffset.UtcNow.ToString("O"));
    }

    /// <summary>Bytes on disk: the database and its write-ahead log, which holds recent changes until a checkpoint.</summary>
    public long SizeOnDisk =>
        new[] { DatabasePath, DatabasePath + "-wal" }.Where(File.Exists).Sum(f => new FileInfo(f).Length);

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
