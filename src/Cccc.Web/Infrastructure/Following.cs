using Cccc.Domain.Model;
using Cccc.Legacy;
using Cccc.Store;

namespace Cccc.Web.Infrastructure;

/// <summary>
/// What following the current app has done lately, for Administration ▸ System and the status bar. It
/// holds no file name and no path: a timetable's file is named after it, and a term can be named after
/// a person.
/// </summary>
public sealed class FollowStatus
{
    /// <summary>Whether this server was started to follow the current app at all (<c>Follow__File</c>).</summary>
    public bool On { get; init; }

    /// <summary>When the file was last read and found good, changed or not.</summary>
    public DateTimeOffset? Checked { get; set; }

    /// <summary>When something last came across, and how many things changed then.</summary>
    public DateTimeOffset? BroughtIn { get; set; }
    public int LastChanges { get; set; }

    /// <summary>When the current app last saved its file, as the file says.</summary>
    public DateTimeOffset? FileSaved { get; set; }

    /// <summary>Why the last look failed, in words for a person; null when it did not.</summary>
    public string? Problem { get; set; }

    /// <summary>Since when the problem has stood: the timetable here is as it was before then.</summary>
    public DateTimeOffset? ProblemSince { get; set; }
}

/// <summary>
/// Keeps one timetable here the same as the current app's own file, so the two can be used side by side
/// with nothing typed twice. It only ever READS that file, which it is given through a read-only mount;
/// it never writes, locks or renames anything of the current app's. The timetable it keeps is read-only
/// here (<see cref="Access.Following"/>): a change made here would be undone by the next save over there.
/// Each save the current app makes is brought across as one change by <see cref="By"/>, so this app's
/// own history, and Now's "changed since yesterday", see the current app's changes like any other.
/// </summary>
public sealed class Follower(string file, ProjectStore store, LiveProjects live, ProjectChoice choice, FollowStatus status,
    Func<DateTimeOffset> clock, Func<DateOnly> today, ILogger<Follower> log)
{
    /// <summary>Whom a change brought across is recorded as. No account can have this sign-in name.</summary>
    public const string By = " current app";

    /// <summary>The name a person reads for <see cref="By"/>.</summary>
    public const string Display = "the current app";

    private const string ProjectKey = "follow_project";
    private const long MaxFile = 64 * 1024 * 1024;

    /// <summary>
    /// The note the laptop's copier (laptop/cccc-next-follow.sh) leaves beside its copy after every look:
    /// <c>ok</c>, <c>missing</c> or <c>failed</c>, and when, in seconds. Following a file directly, as the
    /// preview does, there is none and nothing is asked of it.
    /// </summary>
    public const string CopierNote = "copier";

    /// <summary>The copier looks every fifteen seconds; a note older than this means it has stopped.</summary>
    public static readonly TimeSpan CopierQuiet = TimeSpan.FromMinutes(3);
    private (DateTime Written, long Length)? _seen;

    /// <summary>The timetable kept the same as the current app's, if there is one yet.</summary>
    public static Guid? FollowedId(ProjectStore store) =>
        Guid.TryParse(store.GetMeta(ProjectKey), out var id) && store.Find(id) is { Deleted: null } ? id : null;

    /// <summary>
    /// One look at the file. When it has changed since the last look, it is read and brought across;
    /// when it cannot be read, the timetable stays as it was and the status says why. Never throws for
    /// anything about the file.
    /// </summary>
    public void Check()
    {
        var now = clock();
        // A copier that has stopped, or cannot find the current app's file, leaves its last copy in place,
        // and that copy reads perfectly well: so its own note is asked first, or a stale timetable would be
        // called up to date.
        if (CopierProblem(now) is { } copier) { Fail(now, copier); return; }
        FileInfo info;
        try
        {
            info = new FileInfo(file);
            if (!info.Exists) { Fail(now, "The current app's file is not where Follow__File says."); return; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            Fail(now, "The current app's file cannot be looked at: this app may not be allowed to."); return;
        }
        var stamp = (info.LastWriteTimeUtc, info.Length);
        if (_seen == stamp && status.Problem is null) { status.Checked = now; return; }
        if (info.Length > MaxFile) { Fail(now, "The current app's file is larger than this app reads (64 MB)."); return; }

        byte[] bytes;
        try
        {
            // Shared every way, so a save over there is never held up by a read here.
            using var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            bytes = ms.ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Fail(now, "The current app's file could not be read: this app may not be allowed to."); return;
        }

        Timetable t;
        try
        {
            t = LegacyImport.FromBytes(bytes, today()).Timetable;
        }
        catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException or InvalidOperationException)
        {
            // The old loader's own words (a newer format, say) go on the screen, where only administrators
            // read them. The log gets the kind alone: words made from a file could carry what was in it.
            Fail(now, "The current app's file could not be brought in: " + e.Message, e.GetType().Name); return;
        }

        var project = Project(t, now);
        // The timetable's name here is this app's own (the store keeps it in the settings too); the current
        // app's file has none to give, so following leaves it alone.
        t = t with { Settings = t.Settings with { Name = project.State.Settings.Name } };
        var changed = project.Follow(t, By, "Brought in from the current app");
        _seen = stamp;
        status.Checked = now;
        status.FileSaved = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
        if (status.Problem is not null) log.LogInformation("Following the current app again.");
        status.Problem = null;
        status.ProblemSince = null;
        if (changed.Touched.Count > 0)
        {
            status.BroughtIn = now;
            status.LastChanges = changed.Touched.Count;
            log.LogInformation("Brought in {Count} changes from the current app.", changed.Touched.Count);
        }
    }

    private string? CopierProblem(DateTimeOffset now)
    {
        string text;
        try
        {
            var note = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file)) ?? ".", CopierNote);
            if (!File.Exists(note)) return null;
            text = File.ReadAllText(note).Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return "The copier's note beside the copy could not be read: this app may not be allowed to.";
        }
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !long.TryParse(parts[1], out var seconds))
            return "The copier's note beside the copy is not one this app understands.";
        if (now - DateTimeOffset.FromUnixTimeSeconds(seconds) > CopierQuiet)
            return "The copier on the laptop has not looked at the current app's file for more than three minutes: is it running? (sudo systemctl status cccc-next-follow)";
        return parts[0] switch
        {
            "ok" => null,
            "missing" => "The copier on the laptop cannot find the current app's file. Was its timetable renamed? Step 8 of the runbook says what to change.",
            "failed" => "The copier on the laptop could not copy the current app's file.",
            _ => "The copier's note beside the copy is not one this app understands.",
        };
    }

    private LiveProject Project(Timetable t, DateTimeOffset now)
    {
        if (FollowedId(store) is { } id) return live.Get(id);
        // The first look: the school's timetable arrives here as a timetable of its own and is opened,
        // since following it is what this server was started for. Its name is fixed, never the file's.
        var created = store.Create("The current app's timetable", t, By, now);
        store.SetMeta(ProjectKey, created.ToString());
        log.LogInformation("Following the current app: its timetable is brought in for the first time.");
        choice.Choose(created);
        return live.Get(created);
    }

    /// <param name="kind">For a file that could not be brought in, the kind of failure, which is all the log is told.</param>
    private void Fail(DateTimeOffset now, string why, string? kind = null)
    {
        if (status.Problem != why)
        {
            if (kind is null) log.LogWarning("Following the current app: {Problem}", why);
            else log.LogWarning("Following the current app: its file could not be brought in ({Kind}). Administration ▸ System says why.", kind);
        }
        status.ProblemSince = status.Problem is null ? now : status.ProblemSince;
        status.Problem = why;
    }
}

/// <summary>What a person reads about following, said once.</summary>
public static class FollowWords
{
    public const string Refusal = "This timetable follows the current app: change it there, and it is here within a minute.";

    public const string Notice = "This timetable follows the current app. Change it there: each change is here within a minute.";

    /// <summary>Said when nothing has come across for a while, so what is here may be behind.</summary>
    public static string Behind(FollowStatus s, Func<DateTimeOffset, string> describe) =>
        "This timetable follows the current app, and nothing has come across from it since "
        + (s.Checked is { } good ? describe(good) : "this app started") + ", so it may be behind.";
}

/// <summary>Looks at the current app's file every <c>Follow__EverySeconds</c> (30 unless set), from start-up on.</summary>
public sealed class FollowService(Follower follower, FollowStatus status, IConfiguration config, ILogger<FollowService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        // Off the start-up path: the first look reads and brings in a whole file, and the app answers
        // before it has finished rather than after.
        await Task.Yield();
        var every = TimeSpan.FromSeconds(Math.Clamp(config.GetValue("Follow:EverySeconds", 30), 5, 3600));
        using var timer = new PeriodicTimer(every);
        do
        {
            // Whatever goes wrong, the app keeps running on the timetable it has: an exception leaving a
            // background job stops the whole host. The kind of failure is logged and nothing else.
            try { follower.Check(); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError("Following the current app failed: {Kind}", e.GetType().Name);
                status.ProblemSince ??= DateTimeOffset.UtcNow;
                status.Problem = "Something went wrong bringing in the current app's file (" + e.GetType().Name + ").";
            }
        } while (await timer.WaitForNextTickAsync(stop));
    }
}
