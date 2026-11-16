using System.IO;
using System.Text;
using System.Threading;

namespace CCCCScheduler.Services;

/// <summary>
/// Writes a file so a reader never sees it half-written, and a crash mid-write leaves the previous
/// version intact rather than a truncated one.
///
/// <see cref="File.WriteAllText(string, string)"/> truncates the target and then fills it, so anything
/// that reads concurrently — or any interruption — can observe a partial file. That matters most where
/// the reader falls back to defaults on a parse failure: a truncated <c>settings.json</c> silently wiped
/// the user's recent-projects list and theme with no indication anything had gone wrong.
/// </summary>
public static class AtomicFile
{
    /// <summary>
    /// The retry budget <see cref="Swap"/> gives a caller that does not ask for a shorter one: five
    /// attempts, so four backoffs of 20/40/60/80 ms — up to 200 ms of sleeping before a genuine
    /// failure is allowed to surface.
    /// </summary>
    public const int DefaultAttempts = 5;

    /// <summary>
    /// Called at the end of every completed <see cref="Sync"/>, with the path that was flushed.
    /// <b>A test seam, and nothing in the app sets it.</b>
    ///
    /// <para>Thread-static deliberately. The suite runs test classes in parallel and this class is on
    /// the path of nearly every write in it, so a process-wide field would have one test's observer
    /// firing inside another test's save. <see cref="Swap"/> is synchronous and flushes on its
    /// caller's thread, so a test that sets this sees its own writes and only its own.</para>
    /// </summary>
    [ThreadStatic]
    internal static Action<string>? SyncObserver;

    /// <summary>
    /// Waits for the file's <b>contents</b> to reach the physical disk, and does not return until the
    /// drive says they have.
    ///
    /// <para><b>Why a rename is not enough on its own.</b> <see cref="Swap"/> is a single directory
    /// rename and NTFS journals it — but the journal covers the <i>metadata</i>. The directory entry
    /// can be on the platter while the bytes it points at are still in the write cache, so a power cut
    /// in between lands a correctly-named project file full of zeros: the file the user opens tomorrow
    /// is named right, dated right, and empty. There is no half-written version to fall back on,
    /// because the swap already threw the previous one away.</para>
    ///
    /// <para><b>It is allowed to fail the save.</b> A barrier the volume refuses is not a save that
    /// succeeded quietly — it is a save whose durability nobody can vouch for — so the exception
    /// surfaces, the swap does not happen, and the previous version stays exactly where it was. The
    /// caller's own error path reports it, and nothing has been lost but the last edit.</para>
    /// </summary>
    public static void Sync(string path)
    {
        // FileShare.ReadWrite rather than None: the flush needs a write handle, and something reading
        // the scratch file must not be able to turn a durability barrier into a failed save.
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            fs.Flush(flushToDisk: true);

        SyncObserver?.Invoke(path);
    }

    public static void WriteAllText(string path, string contents, Encoding? encoding = null)
    {
        encoding ??= new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        string dir = Path.GetDirectoryName(path) ?? ".";
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // Beside the target, not in %TEMP%: unique so two processes never fight over one scratch path,
        // and on the same volume so the swap below stays a rename rather than a copy.
        string tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tmp, contents, encoding);
            Swap(tmp, path);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Puts a finished temp file in place under <paramref name="path"/>'s name.
    ///
    /// <para><b>Public because the swap, not the writing, is the shared part.</b>
    /// <see cref="WriteAllText"/> owns both halves for a string. A caller that produced its content
    /// some other way — <c>ProjectPackage.Save</c> writes a ZIP through <c>ZipFile.Open</c>, which
    /// needs a path rather than a string — owns the first half itself and calls this for the second,
    /// rather than hand-rolling the idiom a third time. It was hand-rolled twice before, in
    /// <c>ProjectStore.Save</c> and <c>ProjectPackage.Save</c>, and both copies still carried the
    /// <c>File.Replace</c> defect below long after this class was fixed. That is what three copies of
    /// an idiom costs. <paramref name="tmp"/> must be on the same volume as
    /// <paramref name="path"/> — see the second paragraph.</para>
    ///
    /// <para><b>Why <see cref="File.Move(string, string, bool)"/> and not <see cref="File.Replace(string, string, string)"/>.</b>
    /// <c>File.Replace</c> looks like the atomic-swap primitive and is not one: Win32 <c>ReplaceFile</c>
    /// moves the destination aside and then renames the replacement in, so between those two steps the
    /// destination name cannot be opened. Measured on this machine, a single writer looping over
    /// <c>File.Replace</c> left <c>File.Exists</c> answering <c>false</c> for ~2-9% of concurrent probes,
    /// and a concurrent reader got <see cref="FileNotFoundException"/> from a file that had existed
    /// continuously — mostly ERROR_FILE_NOT_FOUND against a delete-pending destination that was still
    /// listed in the directory. That is the whole failure: not a flaky environment, a non-atomic swap.
    /// Passing a backup name does not close the window (it measured slightly wider).</para>
    ///
    /// <para><c>File.Move(..., overwrite: true)</c> is <c>MoveFileEx</c> with <c>MOVEFILE_REPLACE_EXISTING</c>,
    /// a single directory rename, and showed zero such observations over the same probing. It relies on
    /// the temp file sharing a volume with the target — which is why the temp is created beside it above,
    /// and not in <c>%TEMP%</c>, where the call would quietly degrade to copy-then-delete and hand the
    /// window straight back.</para>
    ///
    /// <para><b>Why the retries stay.</b> They are not covering for the above; they answer a different and
    /// genuinely transient failure. <c>MoveFileEx</c> must delete the destination, so it fails with
    /// ERROR_ACCESS_DENIED while anything else holds it open — an indexer, an AV scanner, a sync client,
    /// or another thread reading settings. Measured against three threads reading the target in a tight
    /// loop: 1502 failures in ~3000 swaps with no retry, and none at all with the backoff below. A
    /// genuine failure (a read-only target, say) still surfaces to the caller after the last attempt.</para>
    ///
    /// <para><b>Why the budget is a parameter.</b> That backoff is a real wait, and a caller on the UI
    /// thread pays it as a freeze. The lock heartbeat is one such caller —
    /// <c>DispatcherPeriodicTimer</c> is a <c>DispatcherTimer</c>, so it ticks on the UI thread — and
    /// measured against a reader spinning on the lock file, the full budget took its worst single write
    /// from 24-33 ms to 146-154 ms, every thirty seconds, for as long as a project is open. It is also
    /// the one caller that does not need the full budget: it repeats every 30 s against a two-minute
    /// staleness window, so the next tick is a better retry than a sleep. See
    /// <c>ConcurrencyService.RefreshLock</c>. A save has no next tick and keeps all five.</para>
    ///
    /// <para><b>The disk barrier goes here, and it defaults to on.</b> <see cref="Sync"/> runs on
    /// <paramref name="tmp"/> before the rename — after the content is written, before it becomes the
    /// file — which is the only point in the sequence where flushing means anything. Doing it here
    /// rather than in each producer is what stops the next caller forgetting: the <c>.cccc</c> does not
    /// come through <see cref="WriteAllText"/> (<c>ZipFile.Open</c> writes to a path), and it is the
    /// one file whose loss is a term's timetable. <paramref name="durable"/> exists for the one write
    /// that is not work — see <c>ConcurrencyService</c> — and every other caller gets the barrier by
    /// saying nothing.</para>
    /// </summary>
    public static void Swap(string tmp, string path, int attempts = DefaultAttempts, bool durable = true)
    {
        if (durable) Sync(tmp);

        for (int i = 1; ; i++)
        {
            try
            {
                // Handles both cases: MOVEFILE_REPLACE_EXISTING is happy whether or not the destination
                // is there, which also removes the File.Exists/act gap the previous two-branch version had.
                File.Move(tmp, path, overwrite: true);
                return;
            }
            catch (Exception e) when (i < attempts && (e is IOException or UnauthorizedAccessException))
            {
                Thread.Sleep(20 * i);
            }
        }
    }
}
