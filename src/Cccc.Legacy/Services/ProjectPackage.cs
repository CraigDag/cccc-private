using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using CCCCScheduler.Models;
using CCCCScheduler.Platform;

namespace CCCCScheduler.Services;

/// <summary>
/// A project on disk is a single <c>.cccc</c> file — a ZIP holding <c>project.json</c> and the floor
/// images. On open it is unpacked into a private temp working directory that the rest of the app treats
/// exactly like the old project folder; on save the working directory is re-zipped back into the one
/// <c>.cccc</c>. Rolling <c>.cccc</c> backups are kept in a <c>backups</c> folder beside the file, and
/// the multi-user advisory lock lives in a sidecar <c>&lt;name&gt;.cccc.lock</c> next to it.
/// </summary>
public static class ProjectPackage
{
    public const string Extension = ".cccc";
    public const string FileFilter = "CCCC Project|*.cccc";
    public const int MaxBackups = 12;

    /// <summary>The sidecar advisory-lock path for a project file.</summary>
    public static string LockPath(string cccPath) => cccPath + ".lock";

    /// <summary>A fresh, empty private working directory under the temp folder.</summary>
    public static string NewWorkingDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "CCCCScheduler", "wd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Unpacks a <c>.cccc</c> into a fresh working directory and returns its path.</summary>
    public static string Open(string cccPath)
    {
        string wd = NewWorkingDir();
        ZipFile.ExtractToDirectory(cccPath, wd, overwriteFiles: true);
        return wd;
    }

    /// <summary>
    /// Zips <c>project.json</c> + <c>images/</c> from the working directory into <paramref name="cccPath"/>
    /// atomically — a scratch file, then the single-rename swap in <see cref="AtomicFile.Swap"/> —
    /// backing up the previous <c>.cccc</c> first. Transient working-dir artefacts (backups/, *.lock)
    /// are deliberately excluded from the package.
    ///
    /// <para><b>This hand-rolled the temp-then-swap idiom</b> and so carried the defect
    /// <see cref="AtomicFile.Swap"/> documents in full: <c>File.Replace</c> is not an atomic swap, and
    /// every save of a <c>.cccc</c> — every autosave included, unattended — left a window in which the
    /// owner's project file could not be opened by name at all. Measured here before the change, with
    /// one writer and no second thread involved: <b>1,178 of 9,849 probes could not open the file,
    /// across 630 saves</b>. Twelve per cent of the time a reader looked, the project was not there.
    /// See <c>ProjectSaveSwapTests</c>.</para>
    ///
    /// <para><b>Why this calls <see cref="AtomicFile.Swap"/> rather than
    /// <see cref="AtomicFile.WriteAllText"/>, unlike <c>ProjectStore.Save</c>.</b> The content here is
    /// not a string, it is an archive. So this owns the first half — produce the finished bytes under a
    /// scratch name — and hands the second half, which is the part that was wrong, to the one
    /// implementation that has been measured.</para>
    ///
    /// <para><b>What the archive holds is decided once</b>, in <see cref="EntriesFrom"/> and written by
    /// <see cref="WriteArchive"/>, which <see cref="SaveToBytes"/> also uses. A second description of
    /// the format — one path-shaped, one byte-shaped — is how the two drift into disagreeing about
    /// entry names or about what is excluded, and a package the other half cannot read is the owner's
    /// term.</para>
    /// </summary>
    /// <param name="keepBackups">
    /// False for internal stores (the crash-recovery snapshot) that are rewritten every couple of
    /// minutes and have no use for rolling history — each rotation there is a full extra copy of the
    /// project that nothing ever cleans up.
    /// </param>
    public static void Save(string workingDir, string cccPath, bool keepBackups = true)
    {
        // Ahead of the zip, not after it: the scratch file is created in this directory, so a missing
        // one used to throw out of ZipFile.Open below and never reach the line that made it.
        string? destDir = Path.GetDirectoryName(cccPath);
        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

        // Unique, like AtomicFile's, and beside the target for the same two reasons: two savers of one
        // project must not fight over one scratch path, and the swap below stays a rename only while
        // the two share a volume. The fixed "<name>.cccc.saving.tmp" this replaces was opened after
        // deleting whatever was already there, so an autosave firing as the user hit Ctrl-S — or a
        // second instance, since the .lock is advisory — could delete the other's half-written zip out
        // from under it. It is still called .saving.tmp so anything that ever appears beside a project
        // still says what it is.
        string tmp = $"{cccPath}.{Guid.NewGuid():N}.saving.tmp";
        try
        {
            // FileMode.CreateNew, write-only, no sharing: exactly what ZipFile.Open(…, Create) opened
            // for itself before the archive writing moved into WriteArchive, which the byte path uses
            // too. The scratch name carries a GUID, so CreateNew's refusal to overwrite is free.
            using (var file = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                WriteArchive(file, EntriesFrom(workingDir));

            // The rotation is a COPY now, and it has to be. File.Replace moved the outgoing file to the
            // backup name as part of the same call; a single rename cannot both put the new file in
            // place and preserve the old one, and the two-rename version — old aside, new in — is
            // precisely the non-atomic swap being removed here. So the previous .cccc is copied while
            // it is still in place and readable throughout, which is exactly the shape ProjectStore.Save
            // has always used for project.json. The copy is not free, but a save already reads and
            // deflates every floor image, so it is not the expensive part of one either.
            string? backup = keepBackups && File.Exists(cccPath) ? TryPrepareBackupPath(cccPath) : null;
            if (backup is not null)
            {
                // Best-effort insurance that must never block a save — the same rule
                // TryPrepareBackupPath states by returning null rather than throwing.
                try { File.Copy(cccPath, backup, overwrite: false); }
                catch { backup = null; }
            }

            try
            {
                AtomicFile.Swap(tmp, cccPath);
            }
            catch
            {
                // A save that fails must not consume a rolling-backup slot: with the copy above made
                // and the swap refused, the file on disk is the one just backed up, so the slot would
                // hold a redundant duplicate of it. Backing up before the replace once flushed an
                // entire history this way — a read-only .cccc, common after restoring from an archive,
                // with the user still working out why saving failed. File.Copy carries the source's
                // attributes across, so a read-only original leaves a read-only copy that cannot be
                // deleted until that is cleared.
                if (backup is not null)
                    try { File.SetAttributes(backup, FileAttributes.Normal); File.Delete(backup); }
                    catch { /* best-effort; the save's own failure is what matters */ }
                throw;
            }

            if (backup is not null)
                Prune(Path.GetDirectoryName(backup)!, Path.GetFileNameWithoutExtension(cccPath));
        }
        finally
        {
            // There was no finally, so a throw partway — a floor image held open by another program is
            // enough — stranded a truncated .saving.tmp beside the project, where it sits looking like
            // the real file until the next save happens to clear it. Swap moves the scratch file when
            // it succeeds, so on the happy path there is nothing here to delete.
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
        }
    }

    // ================================================================================================
    //  The byte-shaped half of the same file format.
    //
    //  A web server has no file paths: what it has is a byte[] coming up out of an HTTP request and a
    //  blob going back down to object storage. Everything below produces and consumes exactly the
    //  .cccc the two desktop apps do — same entry names, same exclusions, same compression, and the
    //  same project.json text, because it goes through the same ProjectStore.SaveTo/LoadFrom and the
    //  same WriteArchive that Save(workingDir, …) above does.
    // ================================================================================================

    /// <summary>
    /// The finished bytes of a <c>.cccc</c> holding <paramref name="project"/> and, optionally, its
    /// floor images — the same archive <see cref="Save(string, string, bool)"/> writes, in memory and
    /// with nothing touching disk.
    ///
    /// <para><paramref name="images"/> is keyed by the entry name inside the package, which is exactly
    /// what <see cref="FloorPlan.ImageFileName"/> holds — <c>images/ground.jpg</c>. A key that is only
    /// a bare file name is accepted and placed under <c>images/</c>; anything that would climb out of
    /// it is reduced to its file name first, for the same reason
    /// <see cref="ProjectStore.ImageFullPath"/> does that on the way back in. Two keys that reduce to
    /// one entry name is a caller bug and throws, rather than one floor plan silently replacing
    /// another's.</para>
    ///
    /// <para><b>A floor that names an image this call was not given is refused, and that guard is why
    /// the parameter is checked at all.</b> The path saver cannot lose a floor plan, because
    /// <see cref="EntriesFrom"/> sweeps <c>images/</c> off the disk; this one packages only what it is
    /// handed. The natural web sequence — <see cref="LoadFromBytes"/>, edit a session,
    /// <c>SaveToBytes(project)</c> — hands it nothing, because <see cref="LoadFromBytes"/> returns the
    /// project and not the images. Left unchecked that writes a package whose every
    /// <see cref="FloorPlan"/> still names a plan the file no longer contains: the plan opens blank,
    /// every room drawn on it sits over nothing, the timetable is perfectly intact and nothing on
    /// screen looks wrong. Pass <see cref="ReadImages(byte[])"/> of the package the project came
    /// from.</para>
    /// </summary>
    public static byte[] SaveToBytes(
        ProjectFile project,
        IReadOnlyDictionary<string, byte[]>? images = null,
        string? savedBy = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        // Both checks run BEFORE ProjectStore.SaveTo, which stamps: a save this refuses must not have
        // left its mark on the caller's project.
        var packaged = NormalisedImages(images);
        RefuseAFloorWhoseImageIsMissing(project, packaged);

        using var buffer = new MemoryStream();
        WriteArchive(buffer, EntriesFor(ProjectStore.SaveTo(project, savedBy), packaged));
        return buffer.ToArray();
    }

    /// <summary>
    /// Refuses a project whose floor plan would not be inside the package it is about to be written
    /// into — an <see cref="ArgumentException"/> the first time a host runs, rather than a blank floor
    /// plan discovered weeks later by the person who drew it.
    /// </summary>
    private static void RefuseAFloorWhoseImageIsMissing(
        ProjectFile project, IReadOnlyDictionary<string, byte[]> images)
    {
        if (project.Building?.Floors is not { } floors) return;

        foreach (var floor in floors)
        {
            if (floor is null || ImageEntryNameFor(floor) is not { } entry) continue;   // no plan drawn
            if (images.ContainsKey(entry)) continue;

            throw new ArgumentException(
                $"The floor \"{floor.Name}\" names the image \"{floor.ImageFileName}\", which is not "
                + $"among the images given to {nameof(SaveToBytes)}. Pass {nameof(ReadImages)} of the "
                + "package this project came from. A package whose floors name images it does not "
                + "contain opens with a blank plan and every room drawn over nothing.",
                nameof(images));
        }
    }

    /// <summary>
    /// The caller's keys reduced to the entry names they will be written under, ordered so two saves
    /// of one project produce the same archive, and with both caller bugs refused here rather than
    /// halfway through writing an archive: a key naming no file, and two keys that become one entry.
    ///
    /// <para>Case-insensitive, to agree with <see cref="ReadImages(Stream)"/> — which has to be,
    /// because <c>ZipFile.ExtractToDirectory</c> on Windows cannot keep <c>ground.png</c> and
    /// <c>Ground.png</c> apart either. Two keys differing only in case are therefore a collision,
    /// rather than two entries that survive one saver and not the other.</para>
    /// </summary>
    private static SortedDictionary<string, byte[]> NormalisedImages(
        IReadOnlyDictionary<string, byte[]>? images)
    {
        var packaged = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (images is null) return packaged;

        foreach (var image in images)
        {
            string name = ImageEntryName(image.Key);
            if (packaged.ContainsKey(name))
                throw new ArgumentException(
                    $"Two images map to the same package entry \"{name}\".", nameof(images));

            packaged[name] = image.Value
                ?? throw new ArgumentException($"Image \"{image.Key}\" has no content.", nameof(images));
        }
        return packaged;
    }

    /// <summary>
    /// Reads a <c>.cccc</c> back out of memory: the project it holds, brought through
    /// <see cref="ProjectStore.LoadFrom"/> — so every repair and <b>every schema migration</b> runs,
    /// exactly as it does when the desktop apps unpack the same file to a working directory.
    /// </summary>
    public static ProjectFile LoadFromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var buffer = new MemoryStream(bytes, writable: false);
        return LoadFrom(buffer);
    }

    /// <summary>
    /// <see cref="LoadFromBytes"/> over a stream, for a host that already has one — an upload, or a
    /// blob it would rather not copy into a second array. The stream is left open.
    /// </summary>
    public static ProjectFile LoadFrom(Stream package)
    {
        ArgumentNullException.ThrowIfNull(package);
        using var zip = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var entry = ProjectJsonEntry(zip)
            ?? throw new InvalidDataException(
                $"This project contains no {ProjectStore.ProjectJsonName}. The file is likely corrupt, "
                + "truncated, or not a CCCC Scheduler project.");

        using var stream = entry.Open();
        // detectEncodingFromByteOrderMarks, like File.ReadAllText on the path side: this build writes
        // UTF-8 with no BOM (see AtomicFile.WriteAllText), but a project.json that arrived from a text
        // editor with one must read the same through both doors.
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return ProjectStore.LoadFrom(reader.ReadToEnd());
    }

    /// <summary>
    /// The floor images inside a <c>.cccc</c>, keyed by their entry name — the same keys
    /// <see cref="SaveToBytes"/> takes, and the same strings
    /// <see cref="FloorPlan.ImageFileName"/> holds, so a caller can go straight from a floor to its
    /// bytes. Empty when the package carries none, which is the normal state of a project whose floor
    /// plan was never drawn.
    ///
    /// <para>Only the top level of <c>images/</c>, because that is all
    /// <see cref="Save(string, string, bool)"/> has ever written — a nested entry in a package from
    /// somewhere else is not a floor plan this app put there.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, byte[]> ReadImages(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var buffer = new MemoryStream(bytes, writable: false);
        return ReadImages(buffer);
    }

    /// <summary><see cref="ReadImages(byte[])"/> over a stream, which is left open.</summary>
    public static IReadOnlyDictionary<string, byte[]> ReadImages(Stream package)
    {
        ArgumentNullException.ThrowIfNull(package);
        using var zip = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var found = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        string prefix = ProjectStore.ImagesDir + "/";
        foreach (var entry in zip.Entries)
        {
            if (entry.Name.Length == 0) continue;                                   // a directory entry
            if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.FullName.IndexOf('/', prefix.Length) >= 0) continue;           // nested, not ours

            using var stream = entry.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            found[entry.FullName] = copy.ToArray();
        }
        return found;
    }

    /// <summary>
    /// The <c>project.json</c> entry, or null. Matched exactly first and then case-insensitively,
    /// because the path side reads it back through <c>File.ReadAllText</c> on Windows, where a package
    /// carrying <c>Project.json</c> opens perfectly well — the two doors must not disagree about which
    /// files are projects.
    /// </summary>
    private static ZipArchiveEntry? ProjectJsonEntry(ZipArchive zip) =>
        zip.GetEntry(ProjectStore.ProjectJsonName)
        ?? zip.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName, ProjectStore.ProjectJsonName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// One thing that goes into a <c>.cccc</c>: its name inside the archive, and where its bytes come
    /// from — a file on disk (<c>SourcePath</c>) or an array already in hand (<c>Bytes</c>). Exactly
    /// one of the two is set.
    /// </summary>
    private readonly record struct PackageEntry(string Name, string? SourcePath, byte[]? Bytes);

    /// <summary>
    /// <b>The last-modified timestamp stamped on every in-memory (<see cref="PackageEntry.Bytes"/>) ZIP
    /// entry — never <c>DateTime.Now</c>, which is what <see cref="ZipArchive.CreateEntry(string,
    /// CompressionLevel)"/> stamps when nothing else is set.</b> That default is what made two saves of
    /// the identical project differ byte for byte: a ZIP local file header's last-mod-time field sits at
    /// a fixed offset (10) near the start of the archive, so <c>DamagedUploadTests</c>' fixed-offset
    /// corruption (<c>bytes.Length / 2</c>) landed on a different byte depending on the exact second the
    /// save happened to run in — the final whole-branch review's diagnosis for a class of browser specs
    /// that flaked without an obvious cause.
    ///
    /// <para>A project the server writes twice must serialise identically both times — see
    /// <c>SampleProjectTests.Build_IsTheSameProjectEveryTime_ToTheByte</c> — and this was the last
    /// source of nondeterminism standing in the way of that for a server save (built from
    /// <see cref="EntriesFor"/>, entirely in memory). Deliberately not "now" and not derived from the
    /// project's own data: nothing reads a ZIP entry's timestamp back, so any fixed value serves —
    /// this one is chosen only to read, on inspection, as an obviously synthetic placeholder rather
    /// than a real save time.</para>
    ///
    /// <para><b>File-backed entries are untouched, on purpose.</b> <see cref="ZipFileExtensions.CreateEntryFromFile(ZipArchive, string, string, CompressionLevel)"/>
    /// — used by <see cref="EntriesFrom"/>, the desktop save's own working-directory path — carries the
    /// source file's REAL last-write time into the entry deliberately, and always has; that is a
    /// different, pre-existing behaviour this fix does not change.</para>
    /// </summary>
    private static readonly DateTimeOffset FixedEntryTimestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// <b>What a <c>.cccc</c> is, written down once.</b> Both savers hand their entries to this, so the
    /// archive's shape — the compression level, the order, the fact that a package is a flat zip and
    /// not a folder of one — cannot come to mean two different things.
    ///
    /// <para>File-backed entries still go through <see cref="ZipFileExtensions.CreateEntryFromFile(ZipArchive, string, string, CompressionLevel)"/>,
    /// which additionally carries the file's last-write time into the entry and, on Unix, its mode.
    /// That is not a second implementation of the format; it is the one call in the framework that
    /// preserves those two, and the desktop save has always preserved them.</para>
    /// </summary>
    private static void WriteArchive(Stream destination, IEnumerable<PackageEntry> entries)
    {
        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var e in entries)
        {
            if (e.SourcePath is not null)
            {
                zip.CreateEntryFromFile(e.SourcePath, e.Name, CompressionLevel.Optimal);
                continue;
            }

            var zipEntry = zip.CreateEntry(e.Name, CompressionLevel.Optimal);
            zipEntry.LastWriteTime = FixedEntryTimestamp;
            using var target = zipEntry.Open();
            target.Write(e.Bytes!, 0, e.Bytes!.Length);
        }
    }

    /// <summary>
    /// What a working directory contributes to the package: <c>project.json</c> if it has one, then
    /// every file directly inside <c>images/</c>. Transient working-dir artefacts — <c>backups/</c>,
    /// <c>*.lock</c>, the scratch files of an interrupted save — are excluded by not being looked for.
    ///
    /// <para><b><c>project.json</c> goes in exactly as it is found</b>, not re-serialised through
    /// <see cref="ProjectStore"/>. <see cref="ConvertLegacyFolder"/> packages a pre-1.0 folder with
    /// this, and conversion is never destructive: a schema-1 folder becomes a schema-1 package, which
    /// then migrates on open like any other old file.</para>
    /// </summary>
    private static IEnumerable<PackageEntry> EntriesFrom(string workingDir)
    {
        string json = Path.Combine(workingDir, ProjectStore.ProjectJsonName);
        if (File.Exists(json))
            yield return new PackageEntry(ProjectStore.ProjectJsonName, json, null);

        string imagesDir = Path.Combine(workingDir, ProjectStore.ImagesDir);
        if (Directory.Exists(imagesDir))
            foreach (var f in Directory.GetFiles(imagesDir))
                yield return new PackageEntry(ImageEntryName(Path.GetFileName(f)), f, null);
    }

    /// <summary>
    /// The same list built from memory. <paramref name="images"/> arrives already reduced to entry
    /// names and already ordered, by <see cref="NormalisedImages"/> — so the rules about what a usable
    /// key is live in one place rather than in an iterator that runs halfway through an archive.
    /// </summary>
    private static IEnumerable<PackageEntry> EntriesFor(
        string projectJson, IReadOnlyDictionary<string, byte[]> images)
    {
        // Encoding.UTF8.GetBytes emits no byte-order mark, which is what AtomicFile.WriteAllText
        // writes on the path side — so the two produce the same project.json to the byte.
        yield return new PackageEntry(
            ProjectStore.ProjectJsonName, null, Encoding.UTF8.GetBytes(projectJson));

        foreach (var image in images)
            yield return new PackageEntry(image.Key, null, image.Value);
    }

    /// <summary>
    /// Where an image goes inside the package: always directly under <c>images/</c>, under its bare
    /// file name. A key carrying directories, a rooted path, or <c>..</c> is reduced to that file name
    /// rather than honoured — the same rule <see cref="ProjectStore.ImageFullPath"/> applies coming
    /// back the other way, and for the same reason: an entry name is a path, and a zip that carries
    /// <c>../../…</c> is one an extractor can be talked into writing outside the folder it was told to
    /// write in.
    /// </summary>
    private static string ImageEntryName(string name)
        => TryImageEntryName(name)
           ?? throw new ArgumentException($"\"{name}\" is not a usable image file name.", nameof(name));

    /// <summary>
    /// <b>Where <paramref name="floor"/>'s plan lives inside a <c>.cccc</c></b> — the key to look it up
    /// by in <see cref="ReadImages(byte[])"/>, and the key to hand <see cref="SaveToBytes"/>.
    /// <c>null</c> when the floor names no image at all, which is the normal state of a floor nobody
    /// has drawn.
    ///
    /// <para><b>This is <see cref="ProjectStore.ImageFullPath"/>'s byte-side twin, and it exists so the
    /// normalisation does not live in only one half.</b> That method reduces a stored
    /// <see cref="FloorPlan.ImageFileName"/> before resolving it on disk, so a <c>project.json</c>
    /// hand-edited to <c>images\ground.png</c> — which the owner has genuinely done, to diagnose a bug
    /// — opens correctly on both desktops. A host doing a plain dictionary lookup on the raw stored
    /// string would miss it and show a blank plan. "The live path scrubs this, the load path does not"
    /// is a whole bug class in this codebase; this is the other half of the sweep.</para>
    /// </summary>
    public static string? ImageEntryNameFor(FloorPlan floor)
    {
        ArgumentNullException.ThrowIfNull(floor);
        return TryImageEntryName(floor.ImageFileName);
    }

    /// <summary>
    /// The single reduction both directions share: separators normalised, everything but the file name
    /// discarded, and <c>null</c> for text naming no file at all. Whether that <c>null</c> is a refusal
    /// or an absence is the caller's question, not this one's — which is why it is not decided here.
    /// </summary>
    private static string? TryImageEntryName(string? name)
    {
        string bare = (name ?? "").Replace('\\', '/');
        int slash = bare.LastIndexOf('/');
        if (slash >= 0) bare = bare[(slash + 1)..];

        return bare.Length == 0 || bare == "." || bare == ".."
            ? null
            : ProjectStore.ImagesDir + "/" + bare;
    }

    /// <summary>
    /// True when <paramref name="path"/> is a pre-1.0 project — a folder holding <c>project.json</c>,
    /// or that <c>project.json</c> itself. Both are accepted because the Open dialog can only select a
    /// file, so the user picks the json, while a command line or drag-drop supplies the folder.
    /// </summary>
    public static bool IsLegacyProjectFolder(string path, out string folder)
    {
        folder = "";
        try
        {
            if (File.Exists(path)
                && string.Equals(Path.GetFileName(path), ProjectStore.ProjectJsonName, StringComparison.OrdinalIgnoreCase))
            {
                folder = Path.GetDirectoryName(path) ?? "";
                return folder.Length > 0;
            }

            if (Directory.Exists(path) && File.Exists(Path.Combine(path, ProjectStore.ProjectJsonName)))
            {
                folder = path;
                return true;
            }
        }
        catch { /* unreadable path — not a legacy project as far as we're concerned */ }

        return false;
    }

    /// <summary>
    /// Packages a pre-1.0 project folder into a <c>.cccc</c> beside it and returns the new file's path.
    /// The original folder is left exactly as it was — conversion is never destructive, and an existing
    /// package is never overwritten.
    /// </summary>
    public static string ConvertLegacyFolder(string folderOrProjectJson)
    {
        if (!IsLegacyProjectFolder(folderOrProjectJson, out string folder))
            throw new InvalidDataException($"\"{folderOrProjectJson}\" is not a CCCC project folder.");

        string parent = Path.GetDirectoryName(folder.TrimEnd(Path.DirectorySeparatorChar))
                        ?? Path.GetDirectoryName(folder) ?? ".";
        string name = Path.GetFileNameWithoutExtension(folder.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name)) name = "CCCC Schedule";

        string dest = Path.Combine(parent, name + Extension);
        for (int n = 2; File.Exists(dest); n++)
            dest = Path.Combine(parent, $"{name} ({n}){Extension}");

        // Reuse the normal package writer so the result is byte-for-byte what the app would have
        // produced itself — same entry names, same exclusions, same atomic swap.
        Save(folder, dest, keepBackups: false);
        return dest;
    }

    /// <summary>True if the path exists and has the <c>.cccc</c> extension.</summary>
    public static bool IsPackage(string path) =>
        File.Exists(path) && string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The first path in <paramref name="candidates"/> this app can actually open, or <c>null</c>.
    ///
    /// <para>This is the decision the app makes about everything the operating system hands it —
    /// <c>argv</c> on Windows, an Apple Event's file list on macOS — and it has to reject as well as
    /// accept: the command line also carries switches, and .NET's <c>GetCommandLineArgs</c> puts the
    /// executable itself at index 0. A pre-1.0 project folder counts, because
    /// <c>OpenProjectFromPath</c> converts one on the way through; a folder is a legitimate argument
    /// for exactly that reason, which is why <see cref="IsPackage"/> alone is not the test.</para>
    ///
    /// <para>Pure, and deliberately so. It was three inline conditions inside a window's private
    /// <c>Loaded</c> handler, where no test could reach it and where the same three conditions would
    /// have had to be written again for every platform that delivers a document differently.</para>
    /// </summary>
    public static string? FirstOpenable(IEnumerable<string> candidates)
    {
        foreach (string path in candidates)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (IsPackage(path) || IsLegacyProjectFolder(path, out _)) return path;
        }
        return null;
    }

    /// <summary>
    /// Every existing file that belongs to a project: the <c>.cccc</c>, its named backups
    /// (<c>backups/&lt;name&gt;-&lt;timestamp&gt;.cccc</c>) and the <c>.cccc.lock</c> sidecar. Backups are
    /// matched strictly (the suffix must be a full <c>-yyyyMMdd-HHmmss-fff</c> stamp) so a delete of
    /// "Term" never grabs the backups of "Term-Extra", "Term-2" or "Term-2026" in the same folder.
    /// </summary>
    public static IReadOnlyList<string> AssociatedFiles(string cccPath)
    {
        var list = new List<string>();
        if (File.Exists(cccPath)) list.Add(cccPath);

        string lockPath = LockPath(cccPath);
        if (File.Exists(lockPath)) list.Add(lockPath);

        string? dir = Path.GetDirectoryName(cccPath);
        string backupsDir = string.IsNullOrEmpty(dir) ? "backups" : Path.Combine(dir, "backups");
        if (Directory.Exists(backupsDir))
        {
            string name = Path.GetFileNameWithoutExtension(cccPath);
            foreach (var f in Directory.GetFiles(backupsDir, name + "-*" + Extension))
                if (IsBackupOf(Path.GetFileName(f), name))
                    list.Add(f);
        }
        return list;
    }

    /// <summary>
    /// Deletes a project and all its associated data to the trash. Returns false if anything
    /// could not be deleted, so callers don't report a success they can't stand behind.
    /// </summary>
    public static bool DeleteProject(ITrash trash, string cccPath)
    {
        bool all = true;
        foreach (var f in AssociatedFiles(cccPath))
            if (!trash.Delete(f)) all = false;
        return all;
    }

    /// <summary>Best-effort delete of a (temp) working directory.</summary>
    public static void DeleteWorkingDir(string? workingDir)
    {
        if (string.IsNullOrEmpty(workingDir)) return;
        try { if (Directory.Exists(workingDir)) Directory.Delete(workingDir, recursive: true); }
        catch { /* temp dirs are best-effort; the OS clears %TEMP% eventually */ }
    }

    /// <summary>
    /// Where the outgoing <c>.cccc</c> should be rotated to, or <c>null</c> to replace without keeping
    /// a backup. Returns null rather than throwing when the backups folder can't be prepared, because
    /// backups are best-effort insurance and must never block a save.
    /// </summary>
    private static string? TryPrepareBackupPath(string cccPath)
    {
        try
        {
            string dir = Path.Combine(Path.GetDirectoryName(cccPath)!, "backups");
            Directory.CreateDirectory(dir);
            string name = Path.GetFileNameWithoutExtension(cccPath);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            string backup = Path.Combine(dir, $"{name}-{stamp}{Extension}");
            return File.Exists(backup) ? null : backup;   // same-millisecond save: skip this rotation
        }
        catch { return null; }
    }

    /// <summary>
    /// True when <paramref name="fileName"/> is a rolling backup belonging to exactly the project
    /// <paramref name="name"/> — that is, <c>&lt;name&gt;-yyyyMMdd-HHmmss-fff.cccc</c>. Requiring the full
    /// timestamp shape (rather than merely "a digit follows the hyphen") is what stops "Term" from
    /// claiming the backups of the sibling projects "Term-Extra", "Term-2" or "Term-2026".
    /// </summary>
    private static bool IsBackupOf(string fileName, string name) =>
        Regex.IsMatch(
            fileName,
            $@"^{Regex.Escape(name)}-\d{{8}}-\d{{6}}-\d{{3}}{Regex.Escape(Extension)}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static void Prune(string dir, string name)
    {
        var files = Directory.GetFiles(dir, $"{name}-*{Extension}")
            .Where(f => IsBackupOf(Path.GetFileName(f), name))
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
        foreach (var old in files.Skip(MaxBackups))
            try { File.Delete(old); } catch { /* ignore */ }
    }
}
