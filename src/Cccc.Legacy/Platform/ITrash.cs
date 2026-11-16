namespace CCCCScheduler.Platform;

/// <summary>
/// Recoverable deletion. Kept behind an interface because the platforms differ sharply: Windows has
/// the Recycle Bin, macOS needs Finder, and neither works on a network share. Implementations must
/// never fall back to a permanent delete — the contract is "recoverable, or not at all".
/// </summary>
public interface ITrash
{
    /// <summary>
    /// True when deleting this path is expected to be recoverable — the closest a platform can get to
    /// a real guarantee, not a certainty. The Windows implementation only checks that the path isn't a
    /// network/UNC location and sits on a local fixed drive; it cannot detect a full Recycle Bin or the
    /// per-drive "delete files immediately, don't move to the Recycle Bin" policy, so a delete can
    /// still turn out to be permanent even when this returns true.
    /// </summary>
    bool Available(string path);

    /// <summary>Deletes recoverably. Returns false if nothing was deleted.</summary>
    bool Delete(string path);
}
